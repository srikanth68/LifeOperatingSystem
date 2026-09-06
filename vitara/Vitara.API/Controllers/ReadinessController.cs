using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;

namespace Vitara.API.Controllers;

[ApiController, Route("api/readiness")]
public class ReadinessController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 14)
    {
        var to   = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-days);
        var data = await repo.GetReadinessAsync(from, to);
        return Ok(data);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int days = 30)
    {
        var to   = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-days);
        var data = await repo.GetReadinessAsync(from, to);
        if (!data.Any()) return Ok(new { count = 0 });

        return Ok(new
        {
            count        = data.Count,
            // How current this actually is. The dashboard has carried daysAgo per
            // metric since it was found reporting last week's sleep as last night's;
            // these summaries never did, and San reads THESE, not the dashboard. An
            // average with no recency reads as today's number no matter how old it is.
            latestDay    = data.Max(x => x.Day).ToString("yyyy-MM-dd"),
            daysAgo      = to.DayNumber - data.Max(x => x.Day).DayNumber,
            // Coverage, so a three-night average is not mistaken for a month of data.
            daysRequested = days,

            avgScore     = data.Where(r => r.Score.HasValue).Select(r => r.Score!.Value).DefaultIfEmpty(0).Average(),
            avgHrvBal    = data.Where(r => r.HrvBalance.HasValue).Select(r => r.HrvBalance!.Value).DefaultIfEmpty(0).Average(),
            avgRhr       = data.Where(r => r.RestingHeartRate.HasValue).Select(r => r.RestingHeartRate!.Value).DefaultIfEmpty(0).Average(),
            levelCounts  = data.GroupBy(r => r.Level ?? "unknown").ToDictionary(g => g.Key, g => g.Count()),
        });
    }
}
