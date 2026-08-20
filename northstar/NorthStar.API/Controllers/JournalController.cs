using Microsoft.AspNetCore.Mvc;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.API.Controllers;

// The user's own daily log, in their own words.
//
// Deliberately NOT a memory. MemoryEntry is FTS-ranked and feeds San's recall on every
// chat turn, and a journal is long, narrative and mostly about a single day — dropping
// it in there would put yesterday's diary in front of the model when the user asks to
// set a reminder. That is exactly how a store of "reminder set for X" lines taught San
// to answer reminder requests with a sentence instead of a tool call, and it is not a
// mistake worth making twice in a new shape.
//
// So the journal is knowledge, dated, and read on purpose rather than retrieved by
// accident. Asking "what did I do last week" fetches it; asking anything else does not.
//
// It also cannot go through /api/ingest, which upserts on (source, topic, day) — right
// for "the AMC bill as of Aug 9", wrong here, where the third thought of the afternoon
// would silently overwrite the first two. Every entry appends.
[ApiController, Route("api/journal")]
public class JournalController(INorthStarRepository repo, ILogger<JournalController> logger) : ControllerBase
{
    public const string Topic = "journal";

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] JournalAddRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Text)) return BadRequest(new { error = "Text is required." });

        // The day is the user's, not the server's: an entry spoken at 11pm belongs to
        // the day being described. The caller (San, which knows the timezone) passes it;
        // UTC today is only the fallback.
        var day = ParseDay(req.Day) ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var entry = await repo.AddEntryAsync(new KnowledgeEntry
        {
            Source = string.IsNullOrWhiteSpace(req.Source) ? "san" : req.Source!,
            Topic = Topic,
            Summary = req.Text.Trim(),
            Day = day,
        });

        logger.LogInformation("Journal entry added for {Day} ({Chars} chars).", day, entry.Summary.Length);
        return Ok(ToResult(entry));
    }

    // Newest first. `days` is a window back from today rather than an explicit range
    // because every real question about a journal is "recently" shaped.
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int days = 14, [FromQuery] int limit = 50)
    {
        var entries = await repo.GetEntriesAsync(source: null, topic: Topic,
            days: Math.Clamp(days, 1, 3650), limit: Math.Clamp(limit, 1, 500));

        return Ok(entries
            .OrderByDescending(e => e.Day ?? DateOnly.FromDateTime(e.CreatedAt))
            .ThenByDescending(e => e.CreatedAt)
            .Select(ToResult));
    }

    private static DateOnly? ParseDay(string? raw)
        => DateOnly.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

    private static object ToResult(KnowledgeEntry e) => new
    {
        e.Id,
        day = e.Day?.ToString("yyyy-MM-dd"),
        text = e.Summary,
        e.Source,
        e.CreatedAt,
    };
}

public record JournalAddRequest(string Text, string? Day, string? Source);
