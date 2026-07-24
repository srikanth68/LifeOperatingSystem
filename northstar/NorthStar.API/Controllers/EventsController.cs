using Microsoft.AspNetCore.Mvc;
using NorthStar.Application.DTOs;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.API.Controllers;

// The event-level activity log. Two audiences:
//   • Producers (Vault, Karma, Sutra, …) POST discrete events here as they happen —
//     one call per transaction / check-in / reminder fired. Ingestion is idempotent
//     on EventKey, so producers may safely retry.
//   • Consumers (San's time-context builder) GET /api/events?since=<ts> to see exactly
//     what happened, with real occurrence timestamps, between two moments.
[ApiController, Route("api/events")]
public class EventsController(INorthStarRepository repo) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] EventRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Source) || string.IsNullOrWhiteSpace(req.Kind))
            return BadRequest("Source and Kind are required.");

        var ev = ToEntity(req);
        var inserted = await repo.AddEventIfNewAsync(ev);
        return Ok(new { inserted, @event = ToResult(ev) });
    }

    [HttpPost("batch")]
    public async Task<IActionResult> PostBatch([FromBody] EventBatchRequest req)
    {
        var evs = req.Events.Where(e => !string.IsNullOrWhiteSpace(e.Source) && !string.IsNullOrWhiteSpace(e.Kind))
                            .Select(ToEntity).ToList();
        var inserted = await repo.AddEventsIfNewAsync(evs);
        return Ok(new { received = evs.Count, inserted });
    }

    // since: ISO-8601 UTC. Defaults to the last 24h if omitted/unparseable — a caller
    // that forgets the cursor still gets a sane recent window rather than everything.
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? since, [FromQuery] string? source, [FromQuery] int limit = 200)
    {
        var sinceUtc = DateTime.TryParse(since, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed) ? parsed : DateTime.UtcNow.AddHours(-24);

        var events = await repo.GetEventsSinceAsync(sinceUtc, source, Math.Clamp(limit, 1, 1000));
        return Ok(new { since = sinceUtc, count = events.Count, events = events.Select(ToResult) });
    }

    private static ActivityEvent ToEntity(EventRequest r) => new()
    {
        Source = r.Source,
        Kind = r.Kind,
        Title = r.Title,
        Detail = r.Detail,
        OccurredAt = (r.OccurredAt ?? DateTime.UtcNow).ToUniversalTime(),
        EventKey = string.IsNullOrWhiteSpace(r.EventKey) ? "" : r.EventKey, // repo derives if blank
        RawJson = r.RawJson,
    };

    private static EventResult ToResult(ActivityEvent e) =>
        new(e.Id, e.Source, e.Kind, e.Title, e.Detail, e.OccurredAt, e.RecordedAt);
}
