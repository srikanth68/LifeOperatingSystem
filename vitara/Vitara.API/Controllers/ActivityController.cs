using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;

namespace Vitara.API.Controllers;

[ApiController, Route("api/activity")]
public class ActivityController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 14)
    {
        var to   = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-days);
        var data = await repo.GetActivityAsync(from, to);
        return Ok(data);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int days = 30)
    {
        var to   = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-days);
        var data = await repo.GetActivityAsync(from, to);
        if (!data.Any()) return Ok(new { count = 0 });

        return Ok(new
        {
            count           = data.Count,
            // How current this actually is. The dashboard has carried daysAgo per
            // metric since it was found reporting last week's sleep as last night's;
            // these summaries never did, and San reads THESE, not the dashboard. An
            // average with no recency reads as today's number no matter how old it is.
            latestDay    = data.Max(x => x.Day).ToString("yyyy-MM-dd"),
            daysAgo      = to.DayNumber - data.Max(x => x.Day).DayNumber,
            // Coverage, so a three-night average is not mistaken for a month of data.
            daysRequested = days,

            avgScore        = data.Where(a => a.Score.HasValue).Select(a => a.Score!.Value).DefaultIfEmpty(0).Average(),
            avgSteps        = data.Average(a => a.Steps),
            avgActiveCalories = data.Average(a => a.ActiveCalories),
            avgHighMin      = data.Average(a => a.HighActivityMinutes),
            avgSedentaryMin = data.Average(a => a.SedentaryMinutes),
        });
    }
}
