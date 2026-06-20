using Microsoft.AspNetCore.Mvc;
using San.Application.DTOs;
using San.Application.Interfaces;
using San.Domain.Entities;

namespace San.API.Controllers;

[ApiController, Route("api/alerts")]
public class AlertsController(ISanRepository repo) : ControllerBase
{
    private static readonly string[] ValidTypes = ["spending_threshold", "goal_deadline", "document_expiry", "custom"];

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok((await repo.GetAlertsAsync()).Select(ToResult));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AlertUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest("Title is required.");
        if (!ValidTypes.Contains(req.Type)) return BadRequest($"Type must be one of: {string.Join(", ", ValidTypes)}");
        if (req.Type == "spending_threshold" && req.ThresholdValue is null) return BadRequest("ThresholdValue is required for spending_threshold alerts.");
        if (req.Type != "spending_threshold" && req.TriggerAt is null) return BadRequest("TriggerAt is required for time-based alerts.");

        var alert = new Alert
        {
            Type = req.Type, Title = req.Title, Description = req.Description ?? "",
            ThresholdValue = req.ThresholdValue, TriggerAt = req.TriggerAt,
            Active = req.Active, NotifyTelegram = req.NotifyTelegram,
        };
        var saved = await repo.AddAlertAsync(alert);
        return Ok(ToResult(saved));
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
        return updated is null ? NotFound() : Ok(ToResult(updated));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteAlertAsync(id) ? NoContent() : NotFound();

    private static AlertResult ToResult(Alert a) =>
        new(a.Id, a.Type, a.Title, a.Description, a.ThresholdValue, a.TriggerAt, a.Active, a.NotifyTelegram, a.TriggeredAt, a.CreatedAt);
}
