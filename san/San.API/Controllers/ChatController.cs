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
        if (string.IsNullOrWhiteSpace(req.Content)) return BadRequest("Message content is required.");

        var turnSw = Stopwatch.StartNew();

        // Timestamp of the last interaction BEFORE this new message — lets San know
        // how long it's been (null if this is the first message ever). Only the newest
        // row is needed; this used to pull fifty to read one field off the last.
        var priorHistory = await repo.GetChatHistoryAsync(1);
        DateTime? lastSeenUtc = priorHistory.Count > 0 ? priorHistory[^1].CreatedAt : null;

        var userMsg = await repo.AddChatMessageAsync(new ChatMessage { Role = "user", Content = req.Content });

        // Fetch generously, then send only what fits the token budget. Bounding by
        // message count meant a turn's prompt size depended entirely on how long the
        // last fifty messages happened to be — a few pasted logs and the context
        // window did the trimming instead, silently and from the wrong end.
        var history = await repo.GetChatHistoryAsync(100);
        var windowed = ChatWindow.Select(history, m => m.Content, m => m.Role);
        var turns = windowed.Select(m => new ChatTurn(m.Role, m.Content)).ToList();

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
        var (tools, executor) = await toolRouter.ResolveAsync(HttpContext.RequestAborted);

        // Order: persona, memory, time awareness, live module snapshot, San's own
        // scheduled items, then (plain LLMs only) the tool instructions. Capabilities
        // and output conventions go last — they're not part of the editable persona,
        // and the model honours late instructions better than ones buried above the
        // snapshot. Keeping them out of the persona also means rewriting the prompt in
        // the UI cannot amputate San's own description of what it can do.
        var capabilities = tools.Count > 0 ? SanCapabilities.Text : null;

        var systemPrompt = string.Join("\n\n",
            new[] { basePrompt, memoryBlock, timeContext, context, ownContext, toolInstructions,
                    capabilities, SanOutputConventions.Text }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

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
            ChatWindow.EstimateTokens(systemPrompt) + turns.Sum(t => ChatWindow.EstimateTokens(t.Content)));

        var (rawReply, llmMs) = await TimedAsync(chat.CompleteWithToolsAsync(systemPrompt, turns, tools, executor));
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
