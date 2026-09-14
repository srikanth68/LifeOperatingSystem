using Microsoft.AspNetCore.Mvc;
using Vitara.Application.DTOs;
using Vitara.Application.Interfaces;

namespace Vitara.API.Controllers;

[ApiController, Route("api/bioage")]
public class BioAgeController(IVitaraRepository repo) : ControllerBase
{
    // Carried in the payload, not only in a UI, so every surface that shows the number --
    // the Vitara and Insight tabs, San, anything later -- carries the same words with it.
    // This is a heuristic built from ring data, and presenting it bare invites reading it
    // as a clinical age.
    public const string Label = "Estimate";
    public const string Disclaimer =
        "A wellness estimate from ring data: how your recovery signals compare with typical values " +
        "for your age. It is not a medical or clinical measurement of biological age.";
    public const string Method =
        "30-day averages of HRV, resting heart rate, sleep score and readiness score, plus the readiness " +
        "trend, are each converted to years above or below your age and blended by weight. When Oura " +
        "reports a cardiovascular age it carries 40% of the weight. The blend is capped at ±15 years.";

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var to   = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-30);

        var profile   = await repo.GetProfileAsync();
        var sleep     = await repo.GetSleepAsync(from, to);
        var readiness = await repo.GetReadinessAsync(from, to);
        var cvAge     = await repo.GetCardiovascularAgeAsync(from, to);
        var vo2       = await repo.GetVo2MaxAsync(from, to);

        if (sleep.Count < 3 && readiness.Count < 3)
            return Ok(new BioAgeResult(null, 0, null, new BioAgeFactors(null, null, null, null, null), "insufficient"));

        var chronoAge = profile?.Age ?? 30;
        var quality   = sleep.Count >= 14 && readiness.Count >= 14 ? "good" : "limited";

        // Use Oura cardiovascular age if available
        var latestCvAge = cvAge.LastOrDefault()?.VascularAge;

        // HRV: population median ~40ms. Higher = younger.
        var hrvValues = sleep.Where(s => s.AvgHrv.HasValue).Select(s => s.AvgHrv!.Value).ToList();
        double? hrvScore = hrvValues.Count > 0 ? hrvValues.Average() : null;
        double? hrvDelta = hrvScore.HasValue ? -(hrvScore.Value - 40.0) / 5.0 : null;

        // RHR: healthy adult avg ~65bpm. Lower = younger.
        var rhrValues = readiness.Where(r => r.RestingHeartRate.HasValue).Select(r => (double)r.RestingHeartRate!.Value).ToList();
        double? rhrScore = rhrValues.Count > 0 ? rhrValues.Average() : null;
        double? rhrDelta = rhrScore.HasValue ? (rhrScore.Value - 65.0) / 3.0 : null;

        // Sleep score
        var sleepScores = sleep.Where(s => s.Score.HasValue).Select(s => (double)s.Score!.Value).ToList();
        double? sleepScore = sleepScores.Count > 0 ? sleepScores.Average() : null;
        double? sleepDelta = sleepScore.HasValue ? -(sleepScore.Value - 75.0) / 5.0 : null;

        // Readiness score
        var readScores = readiness.Where(r => r.Score.HasValue).Select(r => (double)r.Score!.Value).ToList();
        double? readScore = readScores.Count > 0 ? readScores.Average() : null;
        double? readDelta = readScore.HasValue ? -(readScore.Value - 75.0) / 5.0 : null;

        // Recovery trend
        double? recoveryTrend = null;
        if (readiness.Count >= 7)
        {
            var ordered = readiness.Where(r => r.Score.HasValue).OrderBy(r => r.Day).ToList();
            var n  = ordered.Count;
            var xs = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
            var ys = ordered.Select(r => (double)r.Score!.Value).ToArray();
            var xMean = xs.Average(); var yMean = ys.Average();
            var num = xs.Zip(ys).Sum(p => (p.First - xMean) * (p.Second - yMean));
            var den = xs.Sum(x => (x - xMean) * (x - xMean));
            recoveryTrend = den != 0 ? num / den : null;
        }
        double? trendDelta = recoveryTrend.HasValue ? -recoveryTrend.Value * 3.0 : null;

        // If Oura cardiovascular age exists, blend it in (40% weight)
        var factors = latestCvAge.HasValue
            ? new Factor[]
            {
                new("cardiovascular_age", "Cardiovascular age", latestCvAge, "years", latestCvAge.Value - chronoAge, 0.40),
                new("hrv", "HRV", hrvScore, "ms", hrvDelta, 0.15),
                new("resting_hr", "Resting heart rate", rhrScore, "bpm", rhrDelta, 0.15),
                new("sleep_score", "Sleep score", sleepScore, "/100", sleepDelta, 0.15),
                new("readiness_score", "Readiness", readScore, "/100", readDelta, 0.10),
                new("readiness_trend", "Readiness trend", recoveryTrend, "pts/day", trendDelta, 0.05),
            }
            : new Factor[]
            {
                new("hrv", "HRV", hrvScore, "ms", hrvDelta, 0.30),
                new("resting_hr", "Resting heart rate", rhrScore, "bpm", rhrDelta, 0.25),
                new("sleep_score", "Sleep score", sleepScore, "/100", sleepDelta, 0.20),
                new("readiness_score", "Readiness", readScore, "/100", readDelta, 0.15),
                new("readiness_trend", "Readiness trend", recoveryTrend, "pts/day", trendDelta, 0.10),
            };

        var available = factors.Where(f => f.Delta.HasValue).ToList();
        var totalWeight = available.Sum(f => f.Weight);
        double? weightedDelta = available.Count == 0 ? null : available.Sum(f => f.Delta!.Value * f.Weight) / totalWeight;
        double? bioAge = weightedDelta.HasValue ? chronoAge + Math.Clamp(weightedDelta.Value, -15.0, 15.0) : null;

        return Ok(new
        {
            bioAge = bioAge.HasValue ? Math.Round(bioAge.Value, 1) : (double?)null,
            chronologicalAge = chronoAge,
            delta = bioAge.HasValue ? Math.Round(bioAge.Value - chronoAge, 1) : (double?)null,
            cardiovascularAge = latestCvAge.HasValue ? Math.Round(latestCvAge.Value, 1) : (double?)null,
            vo2Max = vo2.LastOrDefault()?.Vo2Max,
            factors = new
            {
                hrvScore = hrvScore.HasValue ? Math.Round(hrvScore.Value, 1) : (double?)null,
                restingHrScore = rhrScore.HasValue ? Math.Round(rhrScore.Value, 1) : (double?)null,
                sleepScore = sleepScore.HasValue ? Math.Round(sleepScore.Value, 1) : (double?)null,
                readinessScore = readScore.HasValue ? Math.Round(readScore.Value, 1) : (double?)null,
                recoveryTrend = recoveryTrend.HasValue ? Math.Round(recoveryTrend.Value, 3) : (double?)null,
            },

            // How much each factor moved the estimate, in years of the final figure --
            // they add up to the unclamped delta. Without this the number is a verdict
            // with no reasons, and the reasons are the useful part: "resting heart rate
            // is adding two years" is something a person can act on.
            contributions = available.Select(f => new
            {
                f.Key,
                f.Name,
                value = Math.Round(f.Value!.Value, f.Unit == "pts/day" ? 3 : 1),
                f.Unit,
                years = Math.Round(f.Delta!.Value * f.Weight / totalWeight, 2),
                weight = Math.Round(f.Weight / totalWeight, 3),
            }),
            clamped = weightedDelta.HasValue && Math.Abs(weightedDelta.Value) > 15.0,

            label = Label,
            disclaimer = Disclaimer,
            method = Method,
            dataQuality = quality,
            ageSource = profile?.Age != null ? "oura" : "config",
        });
    }

    private sealed record Factor(string Key, string Name, double? Value, string Unit, double? Delta, double Weight);

    // Time-series history for the VO2max / cardiovascular-age trend chart.
    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] int days = 90)
    {
        var to   = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-days);
        var cvAge = await repo.GetCardiovascularAgeAsync(from, to);
        var vo2   = await repo.GetVo2MaxAsync(from, to);
        var chrono = (await repo.GetProfileAsync())?.Age;

        return Ok(new
        {
            chronologicalAge = chrono,
            cardiovascularAge = cvAge.Where(c => c.VascularAge.HasValue)
                .Select(c => new { day = c.Day.ToString("yyyy-MM-dd"), value = c.VascularAge }),
            vo2Max = vo2.Where(v => v.Vo2Max.HasValue)
                .Select(v => new { day = v.Day.ToString("yyyy-MM-dd"), value = v.Vo2Max }),
        });
    }
}
