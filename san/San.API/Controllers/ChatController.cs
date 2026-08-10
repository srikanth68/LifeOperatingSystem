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
        "also have a live snapshot of their other modules and the current time, plus the ability to " +
        "create reminders, alerts, and calendar events directly (see the tool instructions below). " +
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
        // how long it's been (null if this is the first message ever).
        var priorHistory = await repo.GetChatHistoryAsync();
        DateTime? lastSeenUtc = priorHistory.Count > 0 ? priorHistory[^1].CreatedAt : null;

        var userMsg = await repo.AddChatMessageAsync(new ChatMessage { Role = "user", Content = req.Content });

        var history = await repo.GetChatHistoryAsync();
        var turns = history.Select(m => new ChatTurn(m.Role, m.Content)).ToList();

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

        // Order: persona, memory, time awareness, live module snapshot, San's own
        // scheduled items, then (plain LLMs only) the tool instructions. Output
        // conventions go last — they're not part of the editable persona, and the
        // model honours late instructions better than ones buried above the snapshot.
        var systemPrompt = string.Join("\n\n",
            new[] { basePrompt, memoryBlock, timeContext, context, ownContext, toolInstructions,
                    SanOutputConventions.Text }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        // One call covers every provider shape: llamacpp-agent overrides
        // CompleteWithToolsAsync and runs a native tool loop with these tools;
        // plain LLM providers use the interface's default, which ignores the
        // tools and falls through to plain CompleteAsync. The router serves the
        // full Maaya.Mcp catalog when the gateway is up, built-ins otherwise.
        var (tools, executor) = await toolRouter.ResolveAsync(HttpContext.RequestAborted);
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
