using Microsoft.AspNetCore.Mvc;
using Vitara.Application.DTOs;
using Vitara.Application.Interfaces;

namespace Vitara.API.Controllers;

[ApiController, Route("api/bioage")]
public class BioAgeController(IVitaraRepository repo, IConfiguration cfg) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var to   = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-30);

        var sleep     = await repo.GetSleepAsync(from, to);
        var readiness = await repo.GetReadinessAsync(from, to);

        if (sleep.Count < 3 && readiness.Count < 3)
            return Ok(new BioAgeResult(null, 0, null, new BioAgeFactors(null, null, null, null, null), "insufficient"));

        var chronoAge = int.TryParse(cfg["Vitara:ChronologicalAge"], out var ca) ? ca : 30;
        var quality   = sleep.Count >= 14 && readiness.Count >= 14 ? "good" : "limited";

        // HRV score: baseline 45ms = 0 delta, every 10ms above = 1 year younger
        var hrvValues = sleep.Where(s => s.AvgHrv.HasValue).Select(s => s.AvgHrv!.Value).ToList();
        double? hrvScore = hrvValues.Count > 0 ? hrvValues.Average() : null;
        double? hrvDelta = hrvScore.HasValue ? -(hrvScore.Value - 45.0) / 10.0 : null;

        // RHR score: baseline 60bpm = 0 delta, every 5bpm lower = 1 year younger
        var rhrValues = readiness.Where(r => r.RestingHeartRate.HasValue).Select(r => (double)r.RestingHeartRate!.Value).ToList();
        double? rhrScore = rhrValues.Count > 0 ? rhrValues.Average() : null;
        double? rhrDelta = rhrScore.HasValue ? (rhrScore.Value - 60.0) / 5.0 : null;

        // Sleep score: baseline 70 = 0 delta, every 10 pts above = 1 year younger
        var sleepScores = sleep.Where(s => s.Score.HasValue).Select(s => (double)s.Score!.Value).ToList();
        double? sleepScore = sleepScores.Count > 0 ? sleepScores.Average() : null;
        double? sleepDelta = sleepScore.HasValue ? -(sleepScore.Value - 70.0) / 10.0 : null;

        // Readiness score: same baseline
        var readScores = readiness.Where(r => r.Score.HasValue).Select(r => (double)r.Score!.Value).ToList();
        double? readScore = readScores.Count > 0 ? readScores.Average() : null;
        double? readDelta = readScore.HasValue ? -(readScore.Value - 70.0) / 10.0 : null;

        // Recovery trend: slope of readiness scores over time (negative = improving)
        double? recoveryTrend = null;
        if (readiness.Count >= 7)
        {
            var ordered = readiness.Where(r => r.Score.HasValue).OrderBy(r => r.Day).ToList();
            var n  = ordered.Count;
            var xs = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
            var ys = ordered.Select(r => (double)r.Score!.Value).ToArray();
            var xMean = xs.Average();
            var yMean = ys.Average();
            var num   = xs.Zip(ys).Sum(p => (p.First - xMean) * (p.Second - yMean));
            var den   = xs.Sum(x => (x - xMean) * (x - xMean));
            recoveryTrend = den != 0 ? num / den : null;
        }
        double? trendDelta = recoveryTrend.HasValue ? -recoveryTrend.Value * 5.0 : null;

        // Composite: average of available deltas
        var deltas = new[] { hrvDelta, rhrDelta, sleepDelta, readDelta, trendDelta }
            .Where(d => d.HasValue).Select(d => d!.Value).ToList();
        double? bioAge = deltas.Count > 0 ? chronoAge + deltas.Average() : null;

        return Ok(new BioAgeResult(
            BioAge: bioAge.HasValue ? Math.Round(bioAge.Value, 1) : null,
            ChronologicalAge: chronoAge,
            Delta: bioAge.HasValue ? Math.Round(bioAge.Value - chronoAge, 1) : null,
            Factors: new BioAgeFactors(
                HrvScore: hrvScore.HasValue ? Math.Round(hrvScore.Value, 1) : null,
                RestingHrScore: rhrScore.HasValue ? Math.Round(rhrScore.Value, 1) : null,
                SleepScore: sleepScore.HasValue ? Math.Round(sleepScore.Value, 1) : null,
                ReadinessScore: readScore.HasValue ? Math.Round(readScore.Value, 1) : null,
                RecoveryTrend: recoveryTrend.HasValue ? Math.Round(recoveryTrend.Value, 3) : null
            ),
            DataQuality: quality
        ));
    }
}
