using Microsoft.AspNetCore.Mvc;
using San.Application;
using San.Application.DTOs;
using San.Application.Interfaces;
using San.Domain.Entities;

namespace San.API.Controllers;

[ApiController, Route("api/alerts")]
public class AlertsController(ISanRepository repo, IModuleContextService moduleContext, ILogger<AlertsController> logger) : ControllerBase
{
    private static readonly string[] ValidTypes = ["spending_threshold", "goal_deadline", "document_expiry", "custom"];

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tz = await moduleContext.ResolveTimeZoneAsync();
        return Ok((await repo.GetAlertsAsync()).Select(a => ToResult(a, tz)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AlertUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest("Title is required.");
        if (!ValidTypes.Contains(req.Type)) return BadRequest($"Type must be one of: {string.Join(", ", ValidTypes)}");
        if (req.Type == "spending_threshold" && req.ThresholdValue is null) return BadRequest("ThresholdValue is required for spending_threshold alerts.");
        if (req.Type != "spending_threshold" && req.TriggerAt is null) return BadRequest("TriggerAt is required for time-based alerts.");

        // Same guard as reminders, same reason: the workers re-notice the same bill on
        // every run and ask for an alert about it in fresh words each time. Title and
        // description are compared together, because the model sometimes puts the
        // identifying detail in one and sometimes in the other.
        if (req.TriggerAt is { } when)
        {
            var live = (await repo.GetAlertsAsync()).Where(a => a.Active && a.TriggerAt is not null);
            var dupe = live.FirstOrDefault(a => DuplicateGuard.IsDuplicate(
                $"{req.Title} {req.Description}".Trim(), when,
                $"{a.Title} {a.Description}".Trim(), a.TriggerAt!.Value));
            if (dupe is not null)
            {
                logger.LogInformation("Alert \"{New}\" already covered by \"{Existing}\" — not creating a second.",
                    req.Title, dupe.Title);
                return Ok(ToResult(dupe, await moduleContext.ResolveTimeZoneAsync()));
            }
        }

        var alert = new Alert
        {
            Type = req.Type, Title = req.Title, Description = req.Description ?? "",
            ThresholdValue = req.ThresholdValue, TriggerAt = req.TriggerAt,
            Active = req.Active, NotifyTelegram = req.NotifyTelegram,
        };
        var saved = await repo.AddAlertAsync(alert);
        return Ok(ToResult(saved, await moduleContext.ResolveTimeZoneAsync()));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AlertUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest("Title is required.");
        if (!ValidTypes.Contains(req.Type)) return BadRequest($"Type must be one of: {string.Join(", ", ValidTypes)}");

        var updated = await repo.UpdateAlertAsync(id, a =>
        {
            a.Type = req.Type; a.Title = req.Title; a.Description = req.Description ?? "";
            a.ThresholdValue = req.ThresholdValue; a.TriggerAt = req.TriggerAt;
            a.Active = req.Active; a.NotifyTelegram = req.NotifyTelegram;
            // Editing re-arms the alert.
            a.TriggeredAt = null;
        });
        return updated is null ? NotFound() : Ok(ToResult(updated, await moduleContext.ResolveTimeZoneAsync()));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteAlertAsync(id) ? NoContent() : NotFound();

    private static AlertResult ToResult(Alert a, TimeZoneInfo tz) =>
        new(a.Id, a.Type, a.Title, a.Description, a.ThresholdValue,
            LocalTimeText.AsUtc(a.TriggerAt), a.Active, a.NotifyTelegram,
            LocalTimeText.AsUtc(a.TriggeredAt), LocalTimeText.AsUtc(a.CreatedAt),
            LocalTimeText.Local(a.TriggerAt, tz));
}
