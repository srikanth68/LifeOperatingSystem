using Microsoft.AspNetCore.Mvc;
using Aasthi.Application.DTOs;
using Aasthi.Application.Interfaces;
using Aasthi.Domain.Entities;

namespace Aasthi.API.Controllers;

[ApiController, Route("api")]
public class MaintenanceController(IAasthiRepository repo) : ControllerBase
{
    [HttpGet("properties/{propertyId:guid}/maintenance")]
    public async Task<IActionResult> List(Guid propertyId)
    {
        var logs = await repo.GetMaintenanceAsync(propertyId);
        return Ok(logs.Select(ToResult));
    }

    [HttpPost("properties/{propertyId:guid}/maintenance")]
    public async Task<IActionResult> Create(Guid propertyId, [FromBody] MaintenanceRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest("Title is required.");
        var log = new MaintenanceLog
        {
            PropertyId    = propertyId,
            Title         = req.Title.Trim(),
            Description   = req.Description?.Trim(),
            VendorName    = req.VendorName?.Trim(),
            VendorContact = req.VendorContact?.Trim(),
            Cost          = req.Cost,
            Category      = string.IsNullOrWhiteSpace(req.Category) ? "repair" : req.Category,
            CompletedDate = req.CompletedDate,
        };
        var created = await repo.AddMaintenanceAsync(log);
        return Created($"/api/maintenance/{created.Id}", ToResult(created));
    }

    [HttpDelete("maintenance/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteMaintenanceAsync(id) ? NoContent() : NotFound();

    [HttpGet("maintenance/summary")]
    public async Task<IActionResult> Summary()
    {
        var properties = await repo.GetPropertiesAsync();
        var logs = await repo.GetMaintenanceAsync();
        var addressById = properties.ToDictionary(p => p.Id, p => p.Address);

        var byCategory = logs.Where(m => m.Cost.HasValue)
            .GroupBy(m => m.Category)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Cost ?? 0));

        var byProperty = logs.Where(m => m.Cost.HasValue)
            .GroupBy(m => m.PropertyId)
            .ToDictionary(
                g => addressById.TryGetValue(g.Key, out var a) ? a : "Unknown",
                g => g.Sum(m => m.Cost ?? 0));

        return Ok(new MaintenanceSummary(
            logs.Sum(m => m.Cost ?? 0),
            byCategory,
            byProperty,
            logs.Count));
    }

    private static MaintenanceResult ToResult(MaintenanceLog m) => new(
        m.Id, m.PropertyId, m.Title, m.Description, m.VendorName, m.VendorContact,
        m.Cost, m.Category, m.CompletedDate, m.CreatedAt);
}
