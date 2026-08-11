using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using NorthStar.Application.DTOs;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.API.Controllers;

// Ingest has two shapes, chosen by whether the caller supplied a DAY:
//
//   source + topic + day  → upsert. One row per (source, topic, day), rewritten in
//                           place as the day's understanding of that topic changes.
//   no day                → append. Every call is its own row, history preserved.
//
// The distinction is the caller telling us what the entry IS. "The AMC bill, as of
// Aug 9" is a fact about a day that gets refined — San's audit and triage restate it
// every 15 minutes while it stays true, and appending each restatement buried the
// brain in near-identical rows that also had to be rate-limited on San's side to stay
// manageable. "San learned the user's policy number" is an event, has no day, and
// keeps its own row forever.
//
// Both existing callers land correctly with no change: ModuleContextService sends
// today's date, AgentToolExecutor's save_knowledge tool sends none.
[ApiController, Route("api/ingest")]
public class IngestController(INorthStarRepository repo, ILogger<IngestController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Ingest([FromBody] IngestRequest req)
    {
        var (entry, created) = await StoreAsync(req);
        await repo.UpsertModuleSyncAsync(new ModuleSync { Module = req.Source, LastSyncAt = DateTime.UtcNow });
        return Ok(ToResult(entry, created));
    }

    [HttpPost("batch")]
    public async Task<IActionResult> IngestBatch([FromBody] IngestBatchRequest req)
    {
        var results = new List<object>();
        var sources = new HashSet<string>();
        var updated = 0;
        foreach (var r in req.Entries)
        {
            var (entry, created) = await StoreAsync(r);
            if (!created) updated++;
            results.Add(ToResult(entry, created));
            sources.Add(r.Source);
        }
        foreach (var src in sources)
            await repo.UpsertModuleSyncAsync(new ModuleSync { Module = src, LastSyncAt = DateTime.UtcNow });
        return Ok(new { ingested = results.Count, created = results.Count - updated, updated, entries = results });
    }

    private async Task<(KnowledgeEntry Entry, bool Created)> StoreAsync(IngestRequest req)
    {
        var day = ParseDay(req.Day);

        if (day is { } d && !string.IsNullOrWhiteSpace(req.Topic))
        {
            var (entry, created) = await repo.UpsertDailyEntryAsync(req.Source, req.Topic, req.Summary, req.RawJson, d);
            if (!created)
                logger.LogInformation("Ingest updated {Source}/{Topic} for {Day} in place.", req.Source, req.Topic, d);
            return (entry, created);
        }

        var appended = new KnowledgeEntry
        {
            Source = req.Source,
            Topic = req.Topic,
            Summary = req.Summary,
            RawJson = req.RawJson,
            Day = day,
        };
        await repo.AddEntryAsync(appended);
        return (appended, true);
    }

    // A malformed date must not 400 the whole ingest — the entry still holds a real
    // summary worth keeping, and the callers are background workers with nobody
    // watching for the error. Falls through to append.
    private DateOnly? ParseDay(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        logger.LogWarning("Ingest received an unparseable day '{Day}' — storing without one.", raw);
        return null;
    }

    // A superset of KnowledgeEntryResult rather than a change to it: `created` is
    // meaningful only when ingesting, and has no business on a search or timeline
    // result. Existing consumers of the old shape keep reading the same fields.
    private static object ToResult(KnowledgeEntry e, bool created) => new
    {
        e.Id, e.Source, e.Topic, e.Summary,
        Day = e.Day?.ToString("yyyy-MM-dd"),
        e.CreatedAt,
        Created = created,
    };
}
