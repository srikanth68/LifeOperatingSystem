using Microsoft.AspNetCore.Mvc;
using Aasthi.Application.DTOs;
using Aasthi.Application.Interfaces;
using Aasthi.Domain.Entities;

namespace Aasthi.API.Controllers;

[ApiController, Route("api/tasks")]
public class TasksController(IAasthiRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? propertyId, [FromQuery] string? status)
    {
        var tasks = await repo.GetTasksAsync(propertyId, status);
        return Ok(tasks.Select(ToResult));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var task = await repo.GetTaskAsync(id);
        return task is null ? NotFound() : Ok(ToResult(task));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TaskUpsertRequest req, [FromQuery] Guid propertyId)
    {
        var task = new PropertyTask
        {
            PropertyId  = propertyId,
            Title       = req.Title,
            Description = req.Description ?? "",
            DueDate     = req.DueDate,
            Priority    = req.Priority ?? "medium",
            Source      = req.Source ?? "manual",
        };
        var created = await repo.AddTaskAsync(task);
        return Created($"/api/tasks/{created.Id}", ToResult(created));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] TaskUpsertRequest req)
    {
        var existing = await repo.GetTaskAsync(id);
        if (existing is null) return NotFound();

        existing.Title       = req.Title;
        existing.Description = req.Description ?? "";
        existing.DueDate     = req.DueDate;
        existing.Priority    = req.Priority ?? existing.Priority;
        existing.Source      = req.Source ?? existing.Source;

        return await repo.UpdateTaskAsync(existing) ? Ok(ToResult(existing)) : NotFound();
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] TaskStatusUpdate req)
    {
        var existing = await repo.GetTaskAsync(id);
        if (existing is null) return NotFound();

        existing.Status = req.Status;
        existing.CompletedAt = req.Status == "completed" ? DateTime.UtcNow : null;

        return await repo.UpdateTaskAsync(existing) ? Ok(ToResult(existing)) : NotFound();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteTaskAsync(id) ? NoContent() : NotFound();

    private static TaskResult ToResult(PropertyTask t) => new(
        t.Id, t.PropertyId, t.Title, t.Description,
        t.DueDate, t.Status, t.Priority, t.Source,
        t.CreatedAt, t.CompletedAt
    );
}
