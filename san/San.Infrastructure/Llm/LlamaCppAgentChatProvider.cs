using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using San.Application;
using San.Application.Interfaces;

namespace San.Infrastructure.Llm;

// Native tool calling against llama.cpp — San runs the agent loop itself.
//
// llama.cpp (started with --jinja) supports the OpenAI `tools` schema on
// /v1/chat/completions: it returns `tool_calls` when the model wants to act, San
// executes them via AgentToolExecutor, appends the results, and loops until the
// model produces a final answer. This replaced the earlier Hermes-gateway route:
// same genuine function calling, but without a third-party harness whose built-in
// toolset cost ~16K prompt tokens per turn (measured 21,344 vs ~5.5K this way)
// and which required a 64K model context.
//
// Config (env):
//   LLM_PROVIDER=llamacpp-agent
//   LLM_BASE_URL=http://host.docker.internal:8080   (llama.cpp on the host)
//   LLM_MODEL=gemma-4                               (echoed; llama.cpp ignores it)
//   LLM_API_KEY=...                                 (only if --api-key was set)
public partial class LlamaCppAgentChatProvider(HttpClient http, IConfiguration config, ILogger<LlamaCppAgentChatProvider> logger) : IChatProvider
{
    public string ProviderName => "llamacpp-agent";
    public string ModelName => Environment.GetEnvironmentVariable("LLM_MODEL")
                               ?? config["Llm:Model"] ?? "gemma-4";

    // Tools ride natively in the request — San must not add the prose action-block
    // scaffolding, nor scrape the reply for one.
    public bool HandlesToolsNatively => true;

    // Slot 0 by default. Override only if something else is using the server's slots;
    // a server started with --parallel 1 has just slot 0, and pinning to it is still
    // correct. An out-of-range slot makes llama.cpp reject the request, so this is
    // deliberately not something to guess at.
    private static int Slot =>
        int.TryParse(Environment.GetEnvironmentVariable("LLM_SLOT"), out var s) && s >= 0 ? s : 0;

    private static string BaseUrl =>
        (Environment.GetEnvironmentVariable("LLM_BASE_URL")
         ?? Environment.GetEnvironmentVariable("LLAMACPP_BASE_URL")
         ?? "http://host.docker.internal:8080").TrimEnd('/');

    public Task<string> CompleteAsync(string systemPrompt, List<ChatTurn> history, CancellationToken ct = default)
        => RunAsync(systemPrompt, history, null, null, 1, false, ct);

    public Task<string> CompleteWithToolsAsync(
        string systemPrompt,
        List<ChatTurn> history,
        List<ToolDefinition> tools,
        Func<ToolCall, CancellationToken, Task<string>> toolExecutor,
        int maxSteps = 10,
        bool enableThinking = false,
        CancellationToken ct = default)
        => RunAsync(systemPrompt, history, tools, toolExecutor, maxSteps, enableThinking, ct);

    private async Task<string> RunAsync(
        string systemPrompt,
        List<ChatTurn> history,
        List<ToolDefinition>? tools,
        Func<ToolCall, CancellationToken, Task<string>>? toolExecutor,
        int maxSteps,
        bool enableThinking,
        CancellationToken ct)
    {
        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        messages.AddRange(history.Select(ToMessage));

        var toolsJson = tools is { Count: > 0 } ? tools.Select(ToOpenAiTool).ToArray() : null;

        // Total-turn timing plus a per-step/per-tool breakdown — a slow reply could be
        // one slow LLM call, several chained tool-calling round trips, or a slow tool
        // (an MCP call fanning out to a sibling module). This is the only place that
        // sees the whole loop, so it's the right place to log where the time actually went.
        var turnSw = Stopwatch.StartNew();

        // Which tools actually ran this turn. The unverified-claim check below is the
        // only thing standing between the user and a confident lie, and it can be
        // trusted only because this list is the loop's own record of what it executed,
        // never the model's account of it.
        var executed = new List<string>();
        var nudged = false;

        for (var step = 0; step < Math.Max(maxSteps, 1); step++)
        {
            var stepSw = Stopwatch.StartNew();
            JsonElement message;
            try
            {
                message = await SendAsync(messages, toolsJson, enableThinking, ct);
            }
            catch (LlmHttpException ex)
            {
                logger.LogWarning("Chat turn failed after {TotalMs}ms at step {Step} ({StepMs}ms): {Message}",
                    turnSw.ElapsedMilliseconds, step, stepSw.ElapsedMilliseconds, ex.UserMessage);
                return ex.UserMessage;
            }
            logger.LogInformation("Step {Step}: LLM call took {StepMs}ms", step, stepSw.ElapsedMilliseconds);

            var toolCalls = message.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array
                ? tc : (JsonElement?)null;

            if (toolCalls is null || toolCalls.Value.GetArrayLength() == 0 || toolExecutor is null)
            {
                var content = message.TryGetProperty("content", out var c) ? c.GetString() : null;

                // Asked to create ten reminders, the model once answered "I have saved 10
                // reminders" having called nothing at all - and nothing in the stack could
                // tell the user otherwise, because a claim in prose is indistinguishable
                // from a real one. It isn't here: the loop knows every tool it ran. When a
                // reply announces a completed write and no write tool ran, give the model
                // exactly one chance to either do the work or take the claim back. The
                // nudge is worded so it stays harmless if the check misfires on a reply
                // that was describing something from an earlier turn.
                if (!nudged && toolExecutor is not null && WriteClaimCheck.ClaimsUnverifiedWrite(content, executed))
                {
                    nudged = true;
                    logger.LogWarning(
                        "Reply claims a completed action but no write tool ran this turn (tools used: {Tools}) - re-prompting once.",
                        executed.Count == 0 ? "none" : string.Join(", ", executed));
                    messages.Add(JsonDocument.Parse(message.GetRawText()).RootElement.Clone());
                    messages.Add(new
                    {
                        role = "user",
                        content =
                            "SYSTEM CHECK: no tool ran during this turn, so nothing was written to Maaya just now. " +
                            "If your previous reply claimed you had just created, saved, scheduled, logged or updated " +
                            "something, that claim is false - either call the correct tool now to actually do it, or " +
                            "correct the statement plainly. If you were describing something from an earlier turn, " +
                            "repeat your answer unchanged.",
                    });
                    continue;
                }

                logger.LogInformation("Chat turn finished after {TotalMs}ms ({Steps} step(s))", turnSw.ElapsedMilliseconds, step + 1);
                return string.IsNullOrWhiteSpace(content)
                    ? "🤔 The local model returned an empty answer. Try rephrasing, or check the model in Settings."
                    : content;
            }

            // Echo the assistant turn (with its tool_calls) back verbatim, then a
            // tool-result message per call — the shape llama.cpp's template expects.
            messages.Add(JsonDocument.Parse(message.GetRawText()).RootElement.Clone());

            foreach (var callEl in toolCalls.Value.EnumerateArray())
            {
                var id = callEl.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                var fn = callEl.GetProperty("function");
                var name = fn.GetProperty("name").GetString() ?? "";
                var args = ParseArguments(fn.TryGetProperty("arguments", out var a) ? a.GetString() : null);

                var toolSw = Stopwatch.StartNew();
                string result;
                try
                {
                    result = await toolExecutor(new ToolCall(name, args), ct);
                }
                catch (Exception ex)
                {
                    result = $"Tool error ({name}): {ex.Message}";
                }
                executed.Add(name);
                logger.LogInformation("Step {Step}: tool {Name} took {ToolMs}ms", step, name, toolSw.ElapsedMilliseconds);

                messages.Add(new { role = "tool", tool_call_id = id, content = result });
            }
        }

        logger.LogWarning("Chat turn stopped after {TotalMs}ms — too many tool steps (max {MaxSteps})", turnSw.ElapsedMilliseconds, maxSteps);
        return "⚠️ San stopped after too many tool steps without a final answer. The actions above may still have run — check the relevant tab.";
    }

    private async Task<JsonElement> SendAsync(List<object> messages, object[]? toolsJson, bool enableThinking, CancellationToken ct)
    {
        // Anonymous types can't hold an optional property, so shape via dictionary.
        var payload = new Dictionary<string, object>
        {
            ["model"] = ModelName,
            ["messages"] = messages,
            ["stream"] = false,
            ["temperature"] = 0.7,
            // This model emits a full step-by-step "should I use a tool?" deliberation
            // (reasoning_content) on every call that offers tools — even when it decides
            // not to use one. Measured: 180 completion tokens (6.4s) vs 12 tokens (0.4s)
            // for the same trivial question, purely from this. Off for interactive chat
            // where that 15x lands directly on the user's wait; on for background work
            // that genuinely has to weigh content before acting (see IChatProvider).
            ["chat_template_kwargs"] = new Dictionary<string, object> { ["enable_thinking"] = enableThinking },
            // Pin every chat turn to the same llama.cpp slot.
            //
            // The prompt cache is PER SLOT. llama-server was running four of them, so
            // consecutive turns landed on whichever was free and each one re-prefilled
            // the entire prompt from cold — measured at ~6.2K tokens and ~30s per turn,
            // of which 3358 tokens were tool schemas that never change between calls.
            // Decode was only 25-45 tokens; almost none of that 30s was generation.
            //
            // Pinning keeps the stable prefix resident in one slot, so a turn re-reads
            // only what actually changed. STT deliberately uses a DIFFERENT slot
            // (see GemmaTranscriber) — sharing one would make each evict the other's
            // cache on every voice turn, which is the worst of both worlds.
            ["id_slot"] = Slot,
            // Stated rather than assumed. Recent llama-server defaults this on, but the
            // whole fix above is worthless if a build defaults it off, and asking for it
            // explicitly costs one field. It is the switch that makes a pinned slot mean
            // anything: without it the slot is reused but the KV cache is not.
            ["cache_prompt"] = true,
        };
        if (toolsJson is not null) payload["tools"] = toolsJson;

        var body = JsonSerializer.Serialize(payload);

        // Exactly what goes on the wire, for when the question is "what did San
        // actually send" rather than "how long did it take". Off unless asked for:
        // this prints the entire prompt, which carries the user's finances, health
        // and correspondence, into the container log.
        if (LogWire)
            logger.LogInformation("LLM REQUEST ({Bytes} bytes, {ToolCount} tools):\n{Body}",
                body.Length, toolsJson?.Length ?? 0, Pretty(body));

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/chat/completions");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct);
        }
        catch
        {
            // Local model unreachable. Stay local — never fall back to a cloud LLM,
            // since the prompt carries private module data.
            throw new LlmHttpException(Offline);
        }

        var replyBody = await resp.Content.ReadAsStringAsync(ct);

        if (LogWire)
            logger.LogInformation("LLM RESPONSE (HTTP {Status}, {Bytes} bytes):\n{Body}",
                (int)resp.StatusCode, replyBody.Length, Pretty(replyBody));

        if (!resp.IsSuccessStatusCode)
            throw new LlmHttpException($"⚠️ San's local model returned an error ({(int)resp.StatusCode}). Nothing left your machine — try again in a moment.");

        try
        {
            return JsonDocument.Parse(replyBody).RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .Clone();
        }
        catch
        {
            throw new LlmHttpException("⚠️ San got an unexpected response from the local model. Nothing left your machine.");
        }
    }

    // Deliberately opt-in and deliberately not Debug: a log level nobody has set is a
    // log nobody finds, and this is only ever wanted while actively looking at it.
    private static bool LogWire =>
        string.Equals(Environment.GetEnvironmentVariable("LLM_LOG_WIRE"), "true",
            StringComparison.OrdinalIgnoreCase);

    // Indented so a prompt is readable in `docker compose logs`. Falls back to the raw
    // string rather than throwing — a logging helper must never break the call it logs.
    //
    // Base64 image payloads are elided: a single attached photo is hundreds of
    // kilobytes of one unbroken line, which buries the prompt this log exists to show
    // and can outrun the log driver entirely.
    private static string Pretty(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var text = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            return DataUrlRegex().Replace(text, m => $"data:{m.Groups[1].Value};base64,<{m.Groups[2].Value.Length} chars elided>");
        }
        catch (JsonException) { return json; }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"data:(image/[a-z+.-]+);base64,([A-Za-z0-9+/=\\]+)")]
    private static partial System.Text.RegularExpressions.Regex DataUrlRegex();

    // A turn with a picture becomes OpenAI's multi-part content array; everything else
    // stays a plain string. Both shapes are valid on the same endpoint, and llama.cpp
    // only accepts the array form when the server was started with a multimodal
    // projector — which `-hf unsloth/gemma-4-*` does automatically (/props reports
    // "vision": true).
    //
    // Image FIRST, then the text. Gemma is markedly better at "look, then read the
    // question" than the reverse, and it matches the order the chat template expects.
    private static object ToMessage(ChatTurn h)
    {
        if (string.IsNullOrWhiteSpace(h.ImageDataUrl))
            return new { role = h.Role, content = h.Content };

        return new
        {
            role = h.Role,
            content = new object[]
            {
                new { type = "image_url", image_url = new { url = h.ImageDataUrl } },
                // A picture sent with no words is still a question — "what is this?" is
                // what the user meant, and an empty text part makes some templates drop
                // the turn entirely.
                new { type = "text", text = string.IsNullOrWhiteSpace(h.Content) ? "What is this?" : h.Content },
            },
        };
    }

    private static object ToOpenAiTool(ToolDefinition t) => new
    {
        type = "function",
        function = new
        {
            name = t.Name,
            description = t.Description,
            parameters = new
            {
                type = "object",
                properties = t.Parameters.ToDictionary(
                    kv => kv.Key,
                    kv => (object)new { type = kv.Value.Type, description = kv.Value.Description }),
                required = t.Parameters.Where(kv => kv.Value.Required).Select(kv => kv.Key).ToArray(),
            },
        },
    };

    // `function.arguments` arrives as a JSON string; flatten every value to a
    // string for ToolCall (numbers/bools keep their raw JSON text).
    private static Dictionary<string, string> ParseArguments(string? raw)
    {
        var dict = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(raw)) return dict;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return dict;
            foreach (var prop in doc.RootElement.EnumerateObject())
                dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? ""
                    : prop.Value.GetRawText();
        }
        catch
        {
            // Model emitted malformed arguments — return empty and let the tool
            // report what's missing, which the model sees and can retry.
        }
        return dict;
    }

    private sealed class LlmHttpException(string userMessage) : Exception(userMessage)
    {
        public string UserMessage => Message;
    }

    private const string Offline =
        "🔌 San's local model is offline right now — the machine hosting Gemma looks asleep or off the Meshnet. " +
        "Your data stayed private (nothing was sent to the cloud). Wake that machine and try again.";
}
