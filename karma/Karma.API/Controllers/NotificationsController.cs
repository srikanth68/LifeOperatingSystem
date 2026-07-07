using Karma.Application.DTOs;
using Karma.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Karma.API.Controllers;

[ApiController, Route("api/notifications")]
public class NotificationsController(INotificationSender sender) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok(new { configured = sender.IsConfigured });

    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] NotificationRequest req)
    {
        if (!sender.IsConfigured)
            return BadRequest("Telegram not configured (TELEGRAM_BOT_TOKEN / TELEGRAM_CHAT_ID missing).");
        await sender.SendAsync(req.Message, req.Channel ?? "telegram");
        return Ok(new { sent = true });
    }

    [HttpPost("habits/{id:guid}/test")]
    public async Task<IActionResult> TestHabit(Guid id, [FromServices] IKarmaRepository repo)
    {
        var habit = await repo.GetHabitAsync(id);
        if (habit is null) return NotFound();
        if (!sender.IsConfigured)
            return BadRequest("Telegram not configured.");
        var msg = habit.NotifyMessage ?? $"{habit.Emoji} <b>{habit.Name}</b> — time to check in!";
        await sender.SendAsync(msg);
        return Ok(new { sent = true, message = msg });
    }
}
