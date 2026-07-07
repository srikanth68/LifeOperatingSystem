using Microsoft.AspNetCore.Mvc;
using NorthStar.Application.DTOs;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.API.Controllers;

[ApiController, Route("api/knowledge")]
public class KnowledgeController(INorthStarRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? source, [FromQuery] string? topic, [FromQuery] int days = 30, [FromQuery] int limit = 200)
    {
        var entries = await repo.GetEntriesAsync(source, topic, days, limit);
        return Ok(entries.Select(ToResult));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var entry = await repo.GetEntryAsync(id);
        return entry is null ? NotFound() : Ok(ToResult(entry));
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(q)) return BadRequest("Query parameter 'q' is required.");
        var entries = await repo.SearchAsync(q, limit);
        return Ok(new SearchResult(entries.Select(ToResult).ToList(), entries.Count, q));
    }

    [HttpGet("timeline")]
    public async Task<IActionResult> Timeline([FromQuery] int days = 7, [FromQuery] int limit = 100)
    {
        var entries = await repo.GetTimelineAsync(days, limit);
        var sourceCounts = entries.GroupBy(e => e.Source).ToDictionary(g => g.Key, g => g.Count());
        var total = await repo.GetEntryCountAsync();
        return Ok(new TimelineResult(entries.Select(ToResult).ToList(), sourceCounts, total));
    }

    private static KnowledgeEntryResult ToResult(KnowledgeEntry e) =>
        new(e.Id, e.Source, e.Topic, e.Summary, e.Day?.ToString("yyyy-MM-dd"), e.CreatedAt);
}
