using Microsoft.AspNetCore.Mvc;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.API.Controllers;

[ApiController, Route("api/facts")]
public class FactsController(INorthStarRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await repo.GetAllFactsAsync());

    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key)
    {
        var f = await repo.GetFactAsync(key);
        return f is not null ? Ok(f) : NotFound();
    }

    // Setting a fact keeps the value it replaces (see FactRevision), and says what that
    // was, so a caller overwriting something it did not know about finds out.
    [HttpPut("{key}")]
    public async Task<IActionResult> Upsert(string key, [FromBody] FactRequest req)
    {
        var previous = (await repo.GetFactAsync(key))?.Value;
        await repo.UpsertFactAsync(new UserFact
        {
            Key = key,
            Value = req.Value,
            Source = req.Source ?? "manual",
            SourceRef = string.IsNullOrWhiteSpace(req.SourceRef) ? null : req.SourceRef.Trim(),
        });
        return Ok(new { key, req.Value, previous = previous != req.Value ? previous : null });
    }

    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key) =>
        await repo.DeleteFactAsync(key) ? NoContent() : NotFound();

    // The current value (if it still exists) and every value before it, newest first.
    [HttpGet("{key}/history")]
    public async Task<IActionResult> History(string key)
    {
        var current = await repo.GetFactAsync(key);
        var previous = await repo.GetFactHistoryAsync(key);
        if (current is null && previous.Count == 0) return NotFound();
        return Ok(new { key, current, previous });
    }
}

public record FactRequest(string Value, string? Source, string? SourceRef = null);
