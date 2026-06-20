using Microsoft.AspNetCore.Mvc;
using San.Application.DTOs;
using San.Application.Interfaces;
using San.Domain.Entities;

namespace San.API.Controllers;

[ApiController, Route("api/chat")]
public class ChatController(ISanRepository repo, IChatProvider chat, IModuleContextService moduleContext) : ControllerBase
{
    private const string SystemPromptPrefix =
        "You are San, the personal life-assistant module inside Maaya OS, a private personal " +
        "dashboard the user built for themselves. You have access to a live snapshot of their " +
        "other modules below. Be concise, warm, and concrete — use the real numbers when relevant. " +
        "If the snapshot says a module is unreachable, just say so plainly instead of guessing.\n\n";

    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages()
    {
        var messages = await repo.GetChatHistoryAsync();
        return Ok(messages.Select(ToResult));
    }

    [HttpPost("messages")]
    public async Task<IActionResult> Send([FromBody] ChatSendRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Content)) return BadRequest("Message content is required.");

        var userMsg = await repo.AddChatMessageAsync(new ChatMessage { Role = "user", Content = req.Content });

        var history = await repo.GetChatHistoryAsync();
        var turns = history.Select(m => new ChatTurn(m.Role, m.Content)).ToList();
        var context = await moduleContext.BuildChatContextAsync();
        var systemPrompt = SystemPromptPrefix + context;

        var replyText = await chat.CompleteAsync(systemPrompt, turns);
        var assistantMsg = await repo.AddChatMessageAsync(new ChatMessage { Role = "assistant", Content = replyText });

        return Ok(new ChatSendResult(ToResult(userMsg), ToResult(assistantMsg), chat.ProviderName, chat.ModelName));
    }

    private static ChatMessageResult ToResult(ChatMessage m) => new(m.Id, m.Role, m.Content, m.CreatedAt);
}
