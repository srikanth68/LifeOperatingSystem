using Microsoft.AspNetCore.Mvc;
using Vitara.Application.DTOs;
using Vitara.Application.Interfaces;

namespace Vitara.API.Controllers;

[ApiController, Route("api/bioage")]
public class BioAgeController(IVitaraRepository repo) : ControllerBase
{
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
        double? bioAge;
        if (latestCvAge.HasValue)
        {
            var cvDelta = latestCvAge.Value - chronoAge;
            var weights = new (double? delta, double weight)[]
            {
                (cvDelta, 0.40), (hrvDelta, 0.15), (rhrDelta, 0.15),
                (sleepDelta, 0.15), (readDelta, 0.10), (trendDelta, 0.05)
            };
            var available = weights.Where(w => w.delta.HasValue).ToList();
            var totalWeight = available.Sum(w => w.weight);
            var weightedDelta = available.Sum(w => w.delta!.Value * w.weight) / totalWeight;
            bioAge = chronoAge + Math.Clamp(weightedDelta, -15.0, 15.0);
        }
        else
        {
            var weights = new (double? delta, double weight)[]
            {
                (hrvDelta, 0.30), (rhrDelta, 0.25), (sleepDelta, 0.20),
                (readDelta, 0.15), (trendDelta, 0.10)
            };
            var available = weights.Where(w => w.delta.HasValue).ToList();
            if (available.Count == 0) { bioAge = null; }
            else
            {
                var totalWeight = available.Sum(w => w.weight);
                var weightedDelta = available.Sum(w => w.delta!.Value * w.weight) / totalWeight;
                bioAge = chronoAge + Math.Clamp(weightedDelta, -15.0, 15.0);
            }
        }

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
            dataQuality = quality,
            ageSource = profile?.Age != null ? "oura" : "config",
        });
    }
}
