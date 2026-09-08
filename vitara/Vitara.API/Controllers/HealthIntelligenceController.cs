using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Health;
using Vitara.Application.Interfaces;
using Vitara.Domain.Health;

namespace Vitara.API.Controllers;

// The read surface for the derived layer.
//
// Everything below was being computed and stored with nothing able to read it: no
// endpoint, so no dashboard, no MCP tool and no way for San to mention any of it. The
// statistics were sound and the system said nothing.
//
// Deliberately separate from the per-metric controllers next to it. Those answer "what
// was my readiness score" from the raw Oura tables; these answer "what is normal for
// me, and is anything off it", which is a different question over different rows.
[ApiController, Route("api/health")]
public class HealthIntelligenceController(IVitaraRepository repo) : ControllerBase
{
    // What Vitara currently believes is going on.
    [HttpGet("findings")]
    public async Task<IActionResult> Findings([FromQuery] bool includeResolved = false, [FromQuery] int limit = 100)
    {
        var findings = await repo.GetFindingsAsync(!includeResolved, limit);
        var today = LocalTime.Today;

        return Ok(findings.Select(f => new
        {
            f.Key,
            f.Type,
            f.Metric,
            f.Direction,
            f.Severity,
            f.Confidence,
            f.Summary,
            firstDetected = f.FirstDetectedLocal.ToString("yyyy-MM-dd"),
            lastDetected = f.LastDetectedLocal.ToString("yyyy-MM-dd"),
            resolved = f.ResolvedLocal?.ToString("yyyy-MM-dd"),
            f.IsActive,

            // How long this has been true. The whole reason FirstDetectedLocal is left
            // alone when a finding continues -- "for the fourth morning" is a different
            // statement from "this morning", and only one of them is worth acting on.
            daysRunning = f.LastDetectedLocal.DayNumber - f.FirstDetectedLocal.DayNumber + 1,

            // Whether it is still current. A finding last detected four days ago is
            // stale even if nothing has formally resolved it, which happens when the
            // worker stops running -- and a stale finding read as current is worse
            // than no finding at all.
            daysSinceDetected = today.DayNumber - f.LastDetectedLocal.DayNumber,

            evidence = f.EvidenceJson,
        }));
    }

    // What normal currently looks like, per metric.
    [HttpGet("baselines")]
    public async Task<IActionResult> Baselines()
    {
        var day = await repo.GetLatestBaselineDayAsync();
        if (day is null) return Ok(new { computedOn = (string?)null, baselines = Array.Empty<object>() });

        var baselines = await repo.GetBaselinesAsync(day.Value);

        return Ok(new
        {
            computedOn = day.Value.ToString("yyyy-MM-dd"),
            daysAgo = LocalTime.Today.DayNumber - day.Value.DayNumber,
            baselines = baselines
                .OrderBy(b => b.Metric)
                .Select(b => new
                {
                    b.Metric,
                    signature = b.BaselineSignature,
                    b.Mean,
                    b.StdDev,
                    b.Median,
                    b.P25,
                    b.P75,
                    b.N,

                    // Exposed rather than filtered out. "Not enough data yet" is a real
                    // answer and a useful one; silently omitting the metric looks
                    // identical to the metric not existing.
                    b.IsValid,
                    b.WindowDays,
                    regimeStart = b.RegimeStartLocal?.ToString("yyyy-MM-dd"),
                    exclusions = b.ExclusionsJson,
                }),
        });
    }

    // Today's z-scores, ratios and slopes, with what fed them.
    [HttpGet("derived")]
    public async Task<IActionResult> Derived([FromQuery] int days = 14)
    {
        var to = LocalTime.Today;
        var derived = await repo.GetDerivedMetricsAsync(to.AddDays(-Math.Clamp(days, 1, 365)), to);

        return Ok(derived.Select(d => new
        {
            d.Metric,
            day = d.ObservedDateLocal.ToString("yyyy-MM-dd"),
            d.Value,

            // Auditability is the point. A derived number nobody can trace back to its
            // inputs is indistinguishable from one the system made up.
            inputs = d.InputsJson,
        }));
    }

    // One call for "how am I doing", shaped for a model rather than a chart.
    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var today = LocalTime.Today;
        var findings = await repo.GetFindingsAsync(activeOnly: true, limit: 50);
        var baselineDay = await repo.GetLatestBaselineDayAsync();
        var latestData = await repo.GetLatestObservationDayAsync();

        var daysBehind = baselineDay is null ? (int?)null : today.DayNumber - baselineDay.Value.DayNumber;

        // Said in words, because the dates alone were not enough.
        //
        // Asked "is anything wrong with my health", San answered "nothing is flagged as
        // being outside your normal range" -- from an empty findings list, on a box
        // where the analysis had not run once. Zero findings and no analysis are the
        // same JSON shape, and the model read the reassuring one. computedThrough and
        // daysBehind were both sitting right there and it went straight past them.
        //
        // A number a model may or may not interpret is not a safeguard. A sentence that
        // says "this has never been computed, do not tell the user they are fine" is.
        var status =
            baselineDay is null
                ? "NO ANALYSIS YET. Baselines have never been computed, so an empty findings list means " +
                  "nothing has been checked - it does NOT mean the user is fine. Say that plainly."
            : daysBehind >= 3
                ? $"STALE. Last computed {daysBehind} days ago, so findings may be out of date. " +
                  "Say so before reporting them."
            : findings.Count == 0
                ? $"Analysis current through {baselineDay:yyyy-MM-dd}. Nothing is outside this user's " +
                  "normal range."
                : $"Analysis current through {baselineDay:yyyy-MM-dd}.";

        return Ok(new
        {
            // Recency first and unavoidably. San reads this, and an analysis with no
            // date attached reads as today's no matter how old it is.
            status,
            latestDataDay = latestData?.ToString("yyyy-MM-dd"),
            computedThrough = baselineDay?.ToString("yyyy-MM-dd"),
            daysBehind,

            activeFindings = findings.Count,
            bySeverity = findings.GroupBy(f => f.Severity).ToDictionary(g => g.Key, g => g.Count()),

            findings = findings
                .OrderByDescending(f => Rank(f.Severity))
                .ThenByDescending(f => f.LastDetectedLocal)
                .Select(f => new
                {
                    f.Key,
                    f.Type,
                    f.Severity,
                    f.Summary,
                    daysRunning = f.LastDetectedLocal.DayNumber - f.FirstDetectedLocal.DayNumber + 1,
                }),
        });
    }

    private static int Rank(string severity) => severity switch
    {
        "high" => 3,
        "notable" => 2,
        _ => 1,
    };
}
