using Microsoft.AspNetCore.Mvc;
using San.Application;
using San.Application.DTOs;
using San.Application.Interfaces;
using San.Domain.Entities;

namespace San.API.Controllers;

[ApiController, Route("api/reminders")]
public class RemindersController(ISanRepository repo, ILogger<RemindersController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok((await repo.GetRemindersAsync()).Select(ToResult));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ReminderUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Text)) return BadRequest("Text is required.");

        // Return the existing one rather than making another.
        //
        // The background workers re-read the same module snapshot every run, so the
        // model asks for "pay the Spectrum bill" again and again, worded differently
        // each time. Three such alerts once existed at once. Every new row is a new
        // notification, which is how a reminder system becomes something you mute.
        //
        // Answering 200 with the record that already covers it is deliberate: the caller
        // wanted the obligation tracked, and it is. An error would only teach an agent
        // to retry with different words.
        var open = (await repo.GetRemindersAsync()).Where(r => !r.Done);
        var existing = open.FirstOrDefault(r => DuplicateGuard.IsDuplicate(req.Text, req.DueAt, r.Text, r.DueAt));
        if (existing is not null)
        {
            logger.LogInformation("Reminder \"{New}\" already covered by \"{Existing}\" — not creating a second.",
                req.Text, existing.Text);
            return Ok(ToResult(existing));
        }

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
