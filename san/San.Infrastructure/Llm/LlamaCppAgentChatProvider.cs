using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
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
public class LlamaCppAgentChatProvider(HttpClient http, IConfiguration config) : IChatProvider
{
    public string ProviderName => "llamacpp-agent";
    public string ModelName => Environment.GetEnvironmentVariable("LLM_MODEL")
                               ?? config["Llm:Model"] ?? "gemma-4";

    // Tools ride natively in the request — San must not add the prose action-block
    // scaffolding, nor scrape the reply for one.
    public bool HandlesToolsNatively => true;

    private static string BaseUrl =>
        (Environment.GetEnvironmentVariable("LLM_BASE_URL")
         ?? Environment.GetEnvironmentVariable("LLAMACPP_BASE_URL")
         ?? "http://host.docker.internal:8080").TrimEnd('/');

    public Task<string> CompleteAsync(string systemPrompt, List<ChatTurn> history, CancellationToken ct = default)
        => RunAsync(systemPrompt, history, null, null, 1, ct);

    public Task<string> CompleteWithToolsAsync(
        string systemPrompt,
        List<ChatTurn> history,
        List<ToolDefinition> tools,
        Func<ToolCall, CancellationToken, Task<string>> toolExecutor,
        int maxSteps = 10,
        CancellationToken ct = default)
        => RunAsync(systemPrompt, history, tools, toolExecutor, maxSteps, ct);

    private async Task<string> RunAsync(
        string systemPrompt,
        List<ChatTurn> history,
        List<ToolDefinition>? tools,
        Func<ToolCall, CancellationToken, Task<string>>? toolExecutor,
        int maxSteps,
        CancellationToken ct)
    {
        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        messages.AddRange(history.Select(h => (object)new { role = h.Role, content = h.Content }));

        var toolsJson = tools is { Count: > 0 } ? tools.Select(ToOpenAiTool).ToArray() : null;

        for (var step = 0; step < Math.Max(maxSteps, 1); step++)
        {
            JsonElement message;
            try
            {
                message = await SendAsync(messages, toolsJson, ct);
            }
            catch (LlmHttpException ex)
            {
                return ex.UserMessage;
            }

            var toolCalls = message.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array
                ? tc : (JsonElement?)null;

            if (toolCalls is null || toolCalls.Value.GetArrayLength() == 0 || toolExecutor is null)
            {
                var content = message.TryGetProperty("content", out var c) ? c.GetString() : null;
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

                string result;
                try
                {
                    result = await toolExecutor(new ToolCall(name, args), ct);
                }
                catch (Exception ex)
                {
                    result = $"Tool error ({name}): {ex.Message}";
                }

                messages.Add(new { role = "tool", tool_call_id = id, content = result });
            }
        }

        return "⚠️ San stopped after too many tool steps without a final answer. The actions above may still have run — check the relevant tab.";
    }

    private async Task<JsonElement> SendAsync(List<object> messages, object[]? toolsJson, CancellationToken ct)
    {
        // Anonymous types can't hold an optional property, so shape via dictionary.
        var payload = new Dictionary<string, object>
        {
            ["model"] = ModelName,
            ["messages"] = messages,
            ["stream"] = false,
            ["temperature"] = 0.7,
        };
        if (toolsJson is not null) payload["tools"] = toolsJson;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/chat/completions");
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

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

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new LlmHttpException($"⚠️ San's local model returned an error ({(int)resp.StatusCode}). Nothing left your machine — try again in a moment.");

        try
        {
            return JsonDocument.Parse(body).RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .Clone();
        }
        catch
        {
            throw new LlmHttpException("⚠️ San got an unexpected response from the local model. Nothing left your machine.");
        }
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
