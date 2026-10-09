using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Health;

namespace Vitara.API.Controllers;

[ApiController, Route("api/sleep")]
public class SleepController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 14)
    {
        var to   = LocalTime.Today;
        var from = to.AddDays(-days);
        var data = await repo.GetSleepAsync(from, to);
        return Ok(data);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int days = 30)
    {
        var to   = LocalTime.Today;
        var from = to.AddDays(-days);
        // Nights, not sessions: a nap counted as a night halves the average and drags the
        // HRV toward the nap's. `count` is the number of nights.
        var data = SleepNights.MainPerDay(await repo.GetSleepAsync(from, to));
        if (!data.Any()) return Ok(new { count = 0 });

        return Ok(new
        {
            count         = data.Count,
            // How current this actually is. The dashboard has carried daysAgo per
            // metric since it was found reporting last week's sleep as last night's;
            // these summaries never did, and San reads THESE, not the dashboard. An
            // average with no recency reads as today's number no matter how old it is.
            latestDay    = data.Max(x => x.Day).ToString("yyyy-MM-dd"),
            daysAgo      = to.DayNumber - data.Max(x => x.Day).DayNumber,
            // Coverage, so a three-night average is not mistaken for a month of data.
            daysRequested = days,

            avgScore      = data.Where(s => s.Score.HasValue).Select(s => s.Score!.Value).DefaultIfEmpty(0).Average(),
            avgHrv        = data.Where(s => s.AvgHrv.HasValue).Select(s => s.AvgHrv!.Value).DefaultIfEmpty(0).Average(),
            avgDeepMin    = data.Average(s => s.DeepMinutes),
            avgRemMin     = data.Average(s => s.RemMinutes),
            avgTotalMin   = data.Average(s => s.TotalSleepMinutes),
            avgEfficiency = data.Average(s => s.Efficiency),
        });
    }
}
