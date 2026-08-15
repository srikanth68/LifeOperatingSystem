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

    // propertyId is accepted from EITHER the query string (what the dashboard sends)
    // or the body (what Maaya.Mcp's property_task_create sends). Binding only the query
    // meant every agent-created task arrived with Guid.Empty and died on the foreign key
    // as a bare HTTP 500 -- which San relayed to the user as "Aasthi had an internal
    // error", making a fixed contract mismatch look like a flaky module. A missing or
    // unknown id is a 400 with a message the agent can act on, never a 500.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TaskUpsertRequest req, [FromQuery] Guid propertyId)
    {
        var id = propertyId != Guid.Empty ? propertyId : req.PropertyId ?? Guid.Empty;
        if (id == Guid.Empty)
            return BadRequest(new { error = "propertyId is required — pass it in the query string or the body." });
        if (await repo.GetPropertyAsync(id) is null)
            return BadRequest(new { error = $"No property with id {id}. List the properties first and use one of those ids." });

        var task = new PropertyTask
        {
            PropertyId  = id,
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
