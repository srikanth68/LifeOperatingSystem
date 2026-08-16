using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using San.Application;
using San.Application.DTOs;
using San.Application.Interfaces;
using San.Domain.Entities;
using San.Infrastructure.Agent;

namespace San.API.Controllers;

[ApiController, Route("api/chat")]
public class ChatController(ISanRepository repo, IChatProvider chat, IModuleContextService moduleContext, IChatActionService actions, AgentToolRouter toolRouter, ILogger<ChatController> logger) : ControllerBase
{
    private const string SystemPromptKey = "chat.system_prompt";

    // Used only until the user sets their own in the editor window.
    public const string DefaultSystemPrompt =
        "You are San, the personal life-assistant module inside Maaya OS, a private personal " +
        "dashboard the user built for themselves. Your long-term memory and brain is NorthStar — " +
        "the relevant things you remember about the user are surfaced below under 'From your " +
        "long-term memory (NorthStar)'. Treat those as things you genuinely know about them. You " +
        "also have a live snapshot of their other modules and the current time, and tools that reach " +
        "every part of their Maaya system — see the capability list below rather than assuming a limit. " +
        "Be concise, warm, and concrete — use the real numbers and remembered facts when relevant. " +
        "If a module is unreachable, just say so plainly instead of guessing.";

    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages()
    {
        var messages = await repo.GetChatHistoryAsync();
        return Ok(messages.Select(ToResult));
    }

    // Clears San's chat history — also shrinks every future turn's prompt to the
    // LLM, since GetChatHistoryAsync resends the last 50 messages each turn.
    [HttpDelete("messages")]
    public async Task<IActionResult> ClearMessages()
    {
        await repo.ClearChatHistoryAsync();
        return NoContent();
    }

    // The editable system prompt, surfaced to the "Edit System Prompt" window.
    [HttpGet("system-prompt")]
    public async Task<IActionResult> GetSystemPrompt()
    {
        var stored = await repo.GetSettingAsync(SystemPromptKey);
        return Ok(new
        {
            prompt = stored ?? DefaultSystemPrompt,
            isDefault = stored is null,
            defaultPrompt = DefaultSystemPrompt,
        });
    }

    [HttpPut("system-prompt")]
    public async Task<IActionResult> SetSystemPrompt([FromBody] SystemPromptRequest req)
    {
        await repo.SetSettingAsync(SystemPromptKey, req.Prompt ?? "");
        return Ok(new { prompt = req.Prompt ?? "" });
    }

    [HttpPost("messages")]
    public async Task<IActionResult> Send([FromBody] ChatSendRequest req)
    {
        var hasImage = !string.IsNullOrWhiteSpace(req.ImageDataUrl);
        // A picture on its own is a complete message — "what is this?" is implied, and
        // the provider fills that in. Only a turn with neither text nor image is empty.
        if (string.IsNullOrWhiteSpace(req.Content) && !hasImage)
            return BadRequest("Message content is required.");

        if (!ImageAttachment.TryValidate(req.ImageDataUrl, out var imageError))
            return BadRequest(imageError);

        // Needed before the history window is chosen, not just at prompt-assembly time:
        // a spoken turn gets a far smaller window (see ChatWindow.VoiceTokenBudget).
        var spoken = string.Equals(req.Mode, "voice", StringComparison.OrdinalIgnoreCase);

        var turnSw = Stopwatch.StartNew();

        // Timestamp of the last interaction BEFORE this new message — lets San know
        // how long it's been (null if this is the first message ever). Only the newest
        // row is needed; this used to pull fifty to read one field off the last.
        var priorHistory = await repo.GetChatHistoryAsync(1);
        DateTime? lastSeenUtc = priorHistory.Count > 0 ? priorHistory[^1].CreatedAt : null;

        // The transcript records that a picture was sent, not the picture. Persisting the
        // base64 would bloat san.db without bound and, worse, ChatWindow would then
        // re-send it inside every later turn's history.
        var userMsg = await repo.AddChatMessageAsync(new ChatMessage
        {
            Role = "user",
            Content = hasImage ? ImageAttachment.DescribeForHistory(req.Content) : req.Content,
        });

        // Fetch generously, then send only what fits the token budget. Bounding by
        // message count meant a turn's prompt size depended entirely on how long the
        // last fifty messages happened to be — a few pasted logs and the context
        // window did the trimming instead, silently and from the wrong end.
        var history = await repo.GetChatHistoryAsync(100);
        var windowed = ChatWindow.Select(history, m => m.Content, m => m.Role,
            spoken ? ChatWindow.VoiceTokenBudget : null);
        var turns = windowed.Select(m => new ChatTurn(m.Role, m.Content)).ToList();

        // Attach the image to the newest user turn — the one just saved. Done here
        // rather than when building the list because history came out of the database,
        // where the image deliberately isn't stored. If windowing dropped the message
        // (it can't today: the newest is always kept) the turn is simply appended.
        if (hasImage)
        {
            var last = turns.FindLastIndex(t => t.Role == "user");
            if (last >= 0) turns[last] = turns[last] with { ImageDataUrl = req.ImageDataUrl };
            else turns.Add(new ChatTurn("user", req.Content, req.ImageDataUrl));
            logger.LogInformation("Chat turn carries an image ({Kb} KB encoded).", req.ImageDataUrl!.Length / 1024);
        }

        if (windowed.Count < history.Count)
            logger.LogInformation("Chat history trimmed to {Kept} of {Total} message(s) (~{Tokens} tokens).",
                windowed.Count, history.Count, windowed.Sum(m => ChatWindow.EstimateTokens(m.Content)));

        // Context-building phase — fans out to every sibling module plus NorthStar
        // memory recall, all before the LLM is ever called. Timed separately from the
        // LLM/tool-loop phase (which LlamaCppAgentChatProvider logs on its own) so a
        // slow reply can be traced to "gathering context" vs "the model/tools".
        var contextSw = Stopwatch.StartNew();
        var basePrompt = await repo.GetSettingAsync(SystemPromptKey) ?? DefaultSystemPrompt;
        var timeContext = await moduleContext.BuildTimeContextAsync(lastSeenUtc);
        var context = await moduleContext.BuildChatContextAsync();
        var ownContext = await actions.BuildOwnContextAsync();
        // Recall relevant long-term memories from NorthStar (San's brain), keyed off
        // what the user just said — so answers are grounded in what San remembers.
        var memories = await moduleContext.RecallMemoriesAsync(req.Content);
        var memoryBlock = string.IsNullOrWhiteSpace(memories) ? null
            : $"From your long-term memory (NorthStar):\n{memories}";
        var contextMs = contextSw.ElapsedMilliseconds;
        logger.LogInformation("Chat context build took {ContextMs}ms", contextMs);

        // A provider with native tool calling (llamacpp-agent) neither needs San's
        // prose action-block instructions nor should its reply be scraped for one.
        // The in-San prose-JSON path is only for plain LLM providers.
        var toolInstructions = chat.HandlesToolsNatively ? null : actions.ToolInstructions;

        // Resolved BEFORE the prompt is assembled: what San is told it can do depends on
        // what it actually has. The router serves the full Maaya.Mcp catalog when the
        // gateway is up and the built-in registry otherwise, and claiming the full set
        // while running on built-ins is exactly the lie that made the silent MCP outage
        // so hard to spot from the outside.
        var (allTools, executor) = await toolRouter.ResolveAsync(HttpContext.RequestAborted);

        // A spoken turn carries a curated subset — see VoiceTools. The executor is
        // unchanged: this narrows what San is OFFERED, not what it could run, so a tool
        // reached some other way still works.
        var tools = spoken ? VoiceTools.Filter(allTools) : allTools;
        if (spoken && tools.Count != allTools.Count)
            logger.LogInformation("Voice turn: offering {Kept} of {Total} tools.", tools.Count, allTools.Count);

        // Order: persona, memory, time awareness, live module snapshot, San's own
        // scheduled items, then (plain LLMs only) the tool instructions. Capabilities
        // and output conventions go last — they're not part of the editable persona,
        // and the model honours late instructions better than ones buried above the
        // snapshot. Keeping them out of the persona also means rewriting the prompt in
        // the UI cannot amputate San's own description of what it can do.
        var capabilities = tools.Count > 0 ? SanCapabilities.Text : null;

        // THE SYSTEM PROMPT MUST NOT CHANGE BETWEEN TURNS. A performance constraint,
        // measured against the live server, not a style preference.
        //
        // llama.cpp caches the prompt prefix per slot and reuses it up to the first byte
        // that differs — and Gemma's template renders the TOOL BLOCK AFTER the system
        // message. So anything volatile in the system prompt sits in front of 3358
        // tokens of tool schemas and forces all of them to be re-read, every turn.
        //
        // Measured on Everest: 6796-token prompt, same slot, caching on.
        //     cold                              22.5s
        //     identical repeat                   1.4s
        //     ONE LINE of the system tail changed  23.6s   <- full re-read
        //     static system, context moved into
        //       the last user message            2.1s / 4.2s / 2.7s
        //
        // That was the whole ~34s voice turn: not the model thinking, not the tools —
        // re-reading an unchanged prompt because one line above it had moved.
        //
        // So everything stable lives in the system prompt, and everything that changes
        // per turn — recalled memory, the time line, the module snapshot — rides in the
        // newest user message instead, landing after the cached region.
        var systemPrompt = string.Join("\n\n",
            new[] { basePrompt, toolInstructions, capabilities,
                    SanOutputConventions.Text, spoken ? SanOutputConventions.Voice : null }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        var liveContext = string.Join("\n\n",
            new[] { memoryBlock, timeContext, context, ownContext }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        // Fenced and labelled, because it arrives where the user's own words go. Without
        // a marker the model answers questions ABOUT the snapshot, or thanks the user for
        // telling it the time.
        if (liveContext.Length > 0)
        {
            var ctxIdx = turns.FindLastIndex(t => t.Role == "user");
            if (ctxIdx >= 0)
                turns[ctxIdx] = turns[ctxIdx] with
                {
                    Content = "[SYSTEM CONTEXT \u2014 background for you, not something the user said]\n"
                              + liveContext
                              + "\n[END CONTEXT]\n\n"
                              + turns[ctxIdx].Content,
                };
        }

        if (spoken) logger.LogInformation("Chat turn arrived by voice — replying for speech.");

        // Where the prompt weight actually goes, per turn. Latency questions about San
        // have so far been answered by guessing at this; now the numbers are in the log
        // next to the timings that they explain.
        logger.LogInformation(
            "Prompt budget (~tokens): persona {Persona}, memory {Memory}, time {Time}, modules {Modules}, " +
            "own {Own}, conventions {Conv}, tools {Tools} ({ToolCount} tools), history {History} → total {Total}",
            ChatWindow.EstimateTokens(basePrompt),
            ChatWindow.EstimateTokens(memoryBlock),
            ChatWindow.EstimateTokens(timeContext),
            ChatWindow.EstimateTokens(context),
            ChatWindow.EstimateTokens(ownContext),
            ChatWindow.EstimateTokens(SanOutputConventions.Text) + ChatWindow.EstimateTokens(capabilities),
            tools.Sum(t => ChatWindow.EstimateTokens(t.Name + t.Description)
                           + t.Parameters.Sum(p => ChatWindow.EstimateTokens(p.Key + p.Value.Description))),
            tools.Count,
            turns.Sum(t => ChatWindow.EstimateTokens(t.Content)),
            // Tools ride in a separate request field, so they were absent from this
            // total while being its largest single component — 3358 of ~6200 tokens.
            // What matters is what the model actually prefills, and reading "total 2841"
            // while the server chewed through 6200 sent a latency hunt the wrong way.
            ChatWindow.EstimateTokens(systemPrompt)
                + turns.Sum(t => ChatWindow.EstimateTokens(t.Content))
                + tools.Sum(t => ChatWindow.EstimateTokens(t.Name + t.Description)
                                 + t.Parameters.Sum(p => ChatWindow.EstimateTokens(p.Key + p.Value.Description))));

        // maxSteps is a per-turn budget of LLM round trips, not of tool calls: the model
        // can fan a batch out across one step, and usually does. But it is free to do them
        // one per step instead, and "create reminders for these ten things" is a real
        // request the user has already made -- at the old ceiling of 10 that lands exactly
        // on the limit, so the last item silently never runs and the turn ends in the
        // too-many-steps warning rather than an answer. The headroom costs nothing on a
        // normal turn, which finishes in one or two steps and never reaches it.
        var (rawReply, llmMs) = await TimedAsync(
            chat.CompleteWithToolsAsync(systemPrompt, turns, tools, executor, maxSteps: 16));
        logger.LogInformation("San raw reply via {Provider} ({Length} chars, {LlmMs}ms): {Preview}",
            chat.ProviderName, rawReply.Length, llmMs, rawReply.Length > 800 ? rawReply[..800] + "…" : rawReply);

        var replyText = chat.HandlesToolsNatively ? rawReply : await actions.ProcessAsync(rawReply);
        if (!chat.HandlesToolsNatively)
            logger.LogInformation("Chat action block detected: {Detected}", replyText != rawReply);

        var assistantMsg = await repo.AddChatMessageAsync(new ChatMessage { Role = "assistant", Content = replyText });

        logger.LogInformation("Chat turn total: {TotalMs}ms (context {ContextMs}ms, llm/tools {LlmMs}ms)",
            turnSw.ElapsedMilliseconds, contextMs, llmMs);

        return Ok(new ChatSendResult(ToResult(userMsg), ToResult(assistantMsg), chat.ProviderName, chat.ModelName));
    }

    private static async Task<(T Result, long Ms)> TimedAsync<T>(Task<T> task)
    {
        var sw = Stopwatch.StartNew();
        var result = await task;
        return (result, sw.ElapsedMilliseconds);
    }

    private static ChatMessageResult ToResult(ChatMessage m) => new(m.Id, m.Role, m.Content, m.CreatedAt);
}

public record SystemPromptRequest(string? Prompt);
