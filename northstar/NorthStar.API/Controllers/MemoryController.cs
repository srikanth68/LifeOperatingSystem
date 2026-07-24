using Microsoft.AspNetCore.Mvc;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.API.Controllers;

// Agent memory API — the store any agent harness (Hermes, openclaw, custom) persists to.
[ApiController, Route("api/memory")]
public class MemoryController(INorthStarRepository repo) : ControllerBase
{
    private static readonly string[] Kinds = ["observation", "preference", "event", "decision", "skill"];

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] MemoryRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Content)) return BadRequest("content is required");
        var kind = (req.Kind ?? "observation").ToLowerInvariant();
        if (!Kinds.Contains(kind)) return BadRequest($"kind must be one of: {string.Join(", ", Kinds)}");

        var m = await repo.SaveMemoryAsync(new MemoryEntry
        {
            Content = req.Content.Trim(),
            Kind = kind,
            Source = req.Source ?? "agent",
            Tags = req.Tags ?? "",
            Importance = Math.Clamp(req.Importance ?? 3, 1, 5),
        });
        return Ok(ToResult(m));
    }

    [HttpGet("recall")]
    public async Task<IActionResult> Recall([FromQuery] string q, [FromQuery] string? kind, [FromQuery] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(q)) return BadRequest("query parameter 'q' is required");
        var results = await repo.RecallMemoriesAsync(q, kind?.ToLowerInvariant(), Math.Clamp(limit, 1, 50));
        return Ok(new { query = q, count = results.Count, memories = results.Select(ToResult) });
    }

    [HttpGet("recent")]
    public async Task<IActionResult> Recent([FromQuery] int limit = 20, [FromQuery] string? kind = null)
    {
        var results = await repo.GetRecentMemoriesAsync(Math.Clamp(limit, 1, 100), kind?.ToLowerInvariant());
        return Ok(results.Select(ToResult));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteMemoryAsync(id) ? NoContent() : NotFound();

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var (total, byKind) = await repo.GetMemoryStatsAsync();
        return Ok(new { total, byKind });
    }

    private static object ToResult(MemoryEntry m) => new
    {
        m.Id, m.Content, m.Kind, m.Source, m.Tags, m.Importance,
        m.CreatedAt, m.LastAccessedAt, m.AccessCount,
    };
}

public record MemoryRequest(string Content, string? Kind, string? Tags, int? Importance, string? Source);
