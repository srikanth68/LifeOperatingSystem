using Microsoft.AspNetCore.Mvc;
using Vitara.Application.DTOs;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;

namespace Vitara.API.Controllers;

[ApiController, Route("api/protocols")]
public class ProtocolsController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);

        var activity  = await repo.GetActivityAsync(to.AddDays(-7), to);
        var sleep     = await repo.GetSleepAsync(to.AddDays(-7), to);
        var readiness = await repo.GetReadinessAsync(to.AddDays(-14), to);

        var protocols = new List<ProtocolResult>
        {
            BuildZone2(activity),
            BuildSleepOptimization(sleep),
            BuildHrvBreathing(readiness),
            new ProtocolResult(
                "Strength Training", "💪", "3× / week",
                "Compound lifts + progressive overload to preserve muscle mass and insulin sensitivity.",
                "manual", null, "Not tracked by Oura — log manually"),
            new ProtocolResult(
                "Time-Restricted Eating", "⏱️", "10:00 – 18:00 window",
                "Eating window drives circadian rhythm, autophagy cycles, and metabolic flexibility.",
                "manual", null, "Not tracked by Oura — log manually"),
        };

        return Ok(protocols);
    }

    // Target: 150 min/week of medium-intensity activity (Oura's "medium activity" zone).
    private static ProtocolResult BuildZone2(List<DailyActivity> activity)
    {
        const string name = "Zone 2 Cardio", icon = "🏃", target = "150 min / week";
        const string desc = "Low-intensity aerobic work for metabolic health, mitochondrial density, and VO₂max.";
        const int targetMin = 150;

        if (activity.Count == 0)
            return new ProtocolResult(name, icon, target, desc, "manual", null, "No activity data synced yet");

        var weekMin = activity.Sum(a => a.MediumActivityMinutes);
        var pct = Math.Min(100, weekMin * 100.0 / targetMin);
        var status = pct >= 70 ? "on-track" : "behind";
        return new ProtocolResult(name, icon, target, desc, status, Math.Round(pct, 0), $"{weekMin} / {targetMin} min this week");
    }

    // Target: 8h/night with a consistent bedtime. Bedtimes are normalized so that
    // times before noon (e.g. 00:15) are treated as "24:15" — most bedtimes cluster
    // in the evening/past-midnight, so this keeps the stddev meaningful instead of
    // splitting a single sleep habit across the midnight boundary.
    private static ProtocolResult BuildSleepOptimization(List<SleepSession> sleep)
    {
        const string name = "Sleep Optimization", icon = "😴", target = "8h · consistent schedule";
        const string desc = "Consistent bedtime, dark cold room, no screens 1h before sleep, morning sunlight.";
        const int targetMin = 480;

        var nights = sleep.Where(s => s.TotalSleepMinutes >= 60).ToList(); // exclude naps
        if (nights.Count == 0)
            return new ProtocolResult(name, icon, target, desc, "manual", null, "No sleep data synced yet");

        var avgMin = nights.Average(s => s.TotalSleepMinutes);
        var bedtimes = nights.Select(s =>
        {
            var m = s.BedtimeStart.Hour * 60 + s.BedtimeStart.Minute;
            return m < 12 * 60 ? m + 24 * 60 : m;
        }).ToList();
        var meanBed = bedtimes.Average();
        var stdBed  = Math.Sqrt(bedtimes.Average(m => Math.Pow(m - meanBed, 2)));

        var durationPct    = Math.Min(100, avgMin * 100.0 / targetMin);
        var consistencyPct = Math.Max(0, 100 - stdBed);
        var pct = (durationPct + consistencyPct) / 2;
        var status = pct >= 70 ? "on-track" : "behind";

        var h = (int)(avgMin / 60);
        var m = (int)(avgMin % 60);
        return new ProtocolResult(name, icon, target, desc, status, Math.Round(pct, 0), $"avg {h}h {m}m · bedtime ±{Math.Round(stdBed)}m");
    }

    // Not directly observable (Oura can't tell if you did breathwork), so this is a
    // recommendation rather than a tracked habit: suggest it when HRV balance over the
    // last 14 days is trending down, since that's the signal coherence breathing targets.
    private static ProtocolResult BuildHrvBreathing(List<DailyReadiness> readiness)
    {
        const string name = "HRV Coherence Breathing", icon = "❤️", target = "5 min · daily";
        const string desc = "5s inhale / 5s exhale to upregulate parasympathetic tone and elevate HRV baseline.";

        var withHrv = readiness.Where(r => r.HrvBalance.HasValue).OrderBy(r => r.Day).ToList();
        if (withHrv.Count < 4)
            return new ProtocolResult(name, icon, target, desc, "manual", null, "Not enough HRV history to evaluate yet");

        var half   = withHrv.Count / 2;
        var prior  = withHrv.Take(half).Average(r => r.HrvBalance!.Value);
        var recent = withHrv.Skip(half).Average(r => r.HrvBalance!.Value);
        var delta  = recent - prior;
        var deltaStr = delta >= 0 ? $"+{delta:F0}" : delta.ToString("F0");

        var status = delta < -3 ? "suggested" : "on-track";
        var metric = delta < -3
            ? $"HRV balance down {Math.Abs(delta):F0} pts vs prior week"
            : $"HRV balance steady ({deltaStr} pts)";

        return new ProtocolResult(name, icon, target, desc, status, null, metric);
    }
}
