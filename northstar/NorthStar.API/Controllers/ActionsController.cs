using Microsoft.AspNetCore.Mvc;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.API.Controllers;

[ApiController, Route("api/actions")]
public class ActionsController(INorthStarRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status = "pending", [FromQuery] int limit = 50)
    {
        var actions = await repo.GetActionsAsync(status == "all" ? null : status, limit);
        return Ok(actions.Select(a => new
        {
            a.Id, a.Source, a.Category, a.Title, a.Description,
            a.Priority, dueDate = a.DueDate?.ToString("yyyy-MM-dd"),
            a.Status, a.ResolvedBy, a.CreatedAt, a.CompletedAt,
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateActionRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest("Title required.");
        var action = new ActionItem
        {
            Source = req.Source ?? "manual",
            Category = req.Category ?? "task",
            Title = req.Title.Trim(),
            Description = req.Description?.Trim(),
            Priority = req.Priority ?? 3,
            DueDate = req.DueDate is not null ? DateOnly.Parse(req.DueDate) : null,
        };
        return Ok(await repo.AddActionAsync(action));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateActionRequest req)
    {
        var result = await repo.UpdateActionAsync(id, req.Status, req.ResolvedBy);
        return result is not null ? Ok(result) : NotFound();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteActionAsync(id) ? NoContent() : NotFound();
}

public record CreateActionRequest(string? Source, string? Category, string Title, string? Description, int? Priority, string? DueDate);
public record UpdateActionRequest(string Status, string? ResolvedBy);
