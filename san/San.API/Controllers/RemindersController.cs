using Microsoft.AspNetCore.Mvc;
using San.Application.DTOs;
using San.Application.Interfaces;
using San.Domain.Entities;

namespace San.API.Controllers;

[ApiController, Route("api/reminders")]
public class RemindersController(ISanRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok((await repo.GetRemindersAsync()).Select(ToResult));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ReminderUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Text)) return BadRequest("Text is required.");
        var reminder = new Reminder { Text = req.Text, DueAt = req.DueAt, NotifyTelegram = req.NotifyTelegram };
        var saved = await repo.AddReminderAsync(reminder);
        return Ok(ToResult(saved));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ReminderUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Text)) return BadRequest("Text is required.");
        var updated = await repo.UpdateReminderAsync(id, r =>
        {
            r.Text = req.Text;
            r.DueAt = req.DueAt;
            r.NotifyTelegram = req.NotifyTelegram;
            if (req.Done.HasValue) r.Done = req.Done.Value;
            // Editing the due date re-arms the Telegram notification.
            r.NotifiedAt = null;
        });
        return updated is null ? NotFound() : Ok(ToResult(updated));
    }

    [HttpPatch("{id:guid}/done")]
    public async Task<IActionResult> SetDone(Guid id, [FromBody] bool done)
    {
        var updated = await repo.UpdateReminderAsync(id, r => r.Done = done);
        return updated is null ? NotFound() : Ok(ToResult(updated));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteReminderAsync(id) ? NoContent() : NotFound();

    private static ReminderResult ToResult(Reminder r) =>
        new(r.Id, r.Text, r.DueAt, r.Done, r.NotifyTelegram, r.NotifiedAt, r.CreatedAt);
}
