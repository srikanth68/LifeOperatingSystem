using System.Globalization;
using System.Text.Json;
using NorthStar.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace NorthStar.API.Controllers;

// A compact week-by-week table of what actually happened, built from ActivityEvent —
// the append-only event log with real occurrence times.
//
// This exists so the MODEL can do the deducing without also being asked to do the
// arithmetic. Handed raw rows, gemma-4-E4B will confidently invent totals, and a
// fabricated correlation lands in NorthStar looking exactly like a real one. So the
// numbers here are computed, and the interesting-ness of them is left entirely to
// San: finding the pattern in this table is the deduction, and it is the model's.
//
// Nothing here decides what matters. It does not rank, threshold, or flag — it counts.
[ApiController, Route("api/rollup")]
public class RollupController(INorthStarRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int weeks = 8)
    {
        var span = Math.Clamp(weeks, 2, 26);
        var since = DateTime.UtcNow.Date.AddDays(-7 * span);

        // High limit: this is an aggregate over everything in the window, and silently
        // truncating the input would produce numbers that look precise and are wrong.
        var events = await repo.GetEventsSinceAsync(since, null, 20000);

        var buckets = events
            .GroupBy(e => WeekStart(e.OccurredAt))
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                weekStarting = g.Key.ToString("yyyy-MM-dd"),
                totalEvents = g.Count(),
                bySource = g.GroupBy(e => e.Source)
                            .ToDictionary(s => s.Key, s => s.Count()),
                byKind = g.GroupBy(e => e.Kind)
                          .ToDictionary(k => k.Key, k => k.Count()),
                // Amounts are parsed out of Detail/RawJson where the producer supplied
                // one. Null (not zero) when a week carries no amounts at all — zero
                // would read as "spent nothing" rather than "nothing recorded".
                spend = Money(g),
            })
            .ToList();

        return Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            weeks = buckets.Count,
            windowStart = since.ToString("yyyy-MM-dd"),
            sources = events.Select(e => e.Source).Distinct().OrderBy(s => s),
            table = buckets,
        });
    }

    // ISO-ish week bucket: Monday of the week the event occurred in.
    private static DateTime WeekStart(DateTime utc)
    {
        var d = utc.Date;
        var offset = ((int)d.DayOfWeek + 6) % 7;   // Monday = 0
        return d.AddDays(-offset);
    }

    private static decimal? Money(IEnumerable<Domain.Entities.ActivityEvent> evs)
    {
        decimal total = 0;
        var found = false;
        foreach (var e in evs)
        {
            if (TryAmount(e.RawJson, out var amt) || TryAmount(e.Detail, out amt))
            {
                total += Math.Abs(amt);
                found = true;
            }
        }
        return found ? Math.Round(total, 2) : null;
    }

    // Tolerant on purpose: producers attach amounts in several shapes, and an amount
    // we cannot read must be skipped rather than guessed at.
    private static bool TryAmount(string? raw, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        if (raw.TrimStart().StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                foreach (var name in new[] { "amount", "Amount", "cost", "Cost", "value" })
                    if (doc.RootElement.TryGetProperty(name, out var el))
                    {
                        if (el.ValueKind == JsonValueKind.Number) { amount = el.GetDecimal(); return true; }
                        if (el.ValueKind == JsonValueKind.String &&
                            decimal.TryParse(el.GetString(), NumberStyles.Currency, CultureInfo.InvariantCulture, out amount))
                            return true;
                    }
            }
            catch (JsonException) { /* not the shape we hoped for — skip */ }
            return false;
        }

        // "Starbucks — $6.40"
        var i = raw.IndexOf('$');
        if (i < 0) return false;
        var tail = new string(raw[(i + 1)..].TakeWhile(c => char.IsDigit(c) || c is '.' or ',').ToArray());
        return decimal.TryParse(tail, NumberStyles.Currency, CultureInfo.InvariantCulture, out amount);
    }
}
