using Microsoft.AspNetCore.Mvc;
using NorthStar.Application.DTOs;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.API.Controllers;

[ApiController, Route("api/ingest")]
public class IngestController(INorthStarRepository repo) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Ingest([FromBody] IngestRequest req)
    {
        var entry = new KnowledgeEntry
        {
            Source = req.Source,
            Topic = req.Topic,
            Summary = req.Summary,
            RawJson = req.RawJson,
            Day = req.Day is not null ? DateOnly.Parse(req.Day) : null
        };
        await repo.AddEntryAsync(entry);
        await repo.UpsertModuleSyncAsync(new ModuleSync { Module = req.Source, LastSyncAt = DateTime.UtcNow });
        return Ok(ToResult(entry));
    }

    [HttpPost("batch")]
    public async Task<IActionResult> IngestBatch([FromBody] IngestBatchRequest req)
    {
        var results = new List<KnowledgeEntryResult>();
        var sources = new HashSet<string>();
        foreach (var r in req.Entries)
        {
            var entry = new KnowledgeEntry
            {
                Source = r.Source,
                Topic = r.Topic,
                Summary = r.Summary,
                RawJson = r.RawJson,
                Day = r.Day is not null ? DateOnly.Parse(r.Day) : null
            };
            await repo.AddEntryAsync(entry);
            results.Add(ToResult(entry));
            sources.Add(r.Source);
        }
        foreach (var src in sources)
            await repo.UpsertModuleSyncAsync(new ModuleSync { Module = src, LastSyncAt = DateTime.UtcNow });
        return Ok(new { ingested = results.Count, entries = results });
    }

    private static KnowledgeEntryResult ToResult(KnowledgeEntry e) =>
        new(e.Id, e.Source, e.Topic, e.Summary, e.Day?.ToString("yyyy-MM-dd"), e.CreatedAt);
}
