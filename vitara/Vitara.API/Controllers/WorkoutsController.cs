using Microsoft.AspNetCore.Mvc;
using Vitara.Application;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;

namespace Vitara.API.Controllers;

[ApiController, Route("api/workouts")]
public class WorkoutsController(IVitaraRepository repo) : ControllerBase
{
    // A workout's Day is a local-calendar fact, so both the default and the query
    // window use local time (containers run TZ=America/New_York). Computed in UTC, an
    // evening workout logged after 8pm Eastern was filed under TOMORROW — and the
    // window ended "today" in UTC, which is a different day from the one the user is
    // standing in.
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 30)
    {
        var to = DateOnly.FromDateTime(DateTime.Now);
        // +1 tolerates anything already filed under tomorrow by an earlier build.
        return Ok(await repo.GetWorkoutsAsync(to.AddDays(-days), to.AddDays(1)));
    }

    // How each kind of session shows up in the next morning's readiness. Needs a
    // longer window than the dashboard's 30 days: the analysis compares training
    // mornings against REST mornings, and a short window on a consistent training week
    // can contain too few rest days to say anything at all.
    [HttpGet("impact")]
    public async Task<IActionResult> Impact([FromQuery] int days = 90)
    {
        var to = DateOnly.FromDateTime(DateTime.Now);
        var from = to.AddDays(-days);
        var workouts = await repo.GetWorkoutsAsync(from, to.AddDays(1));
        // One day past the window on each side: a workout on the first day is only
        // interpretable if the morning after it is in range.
        var readiness = await repo.GetReadinessAsync(from, to.AddDays(1));
        return Ok(WorkoutImpact.Analyze(workouts, readiness));
    }

    [HttpPost]
    public async Task<IActionResult> Log([FromBody] WorkoutRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Activity)) return BadRequest("Activity is required.");
        if (!string.IsNullOrWhiteSpace(req.Day) && !DateOnly.TryParse(req.Day, out _))
            return BadRequest($"Could not read '{req.Day}' as a date — use yyyy-MM-dd.");

        var day = string.IsNullOrWhiteSpace(req.Day)
            ? DateOnly.FromDateTime(DateTime.Now)
            : DateOnly.Parse(req.Day);

        var workout = new Workout
        {
            Id = Guid.NewGuid().ToString(),
            Day = day,
            Activity = req.Activity.Trim(),
            StartTime = req.StartTime,
            EndTime = req.EndTime,
            Calories = req.Calories,
            Distance = req.Distance,
            Intensity = req.Intensity,
            Label = req.Label,
            Source = "manual",
        };
        await repo.UpsertWorkoutsAsync(new[] { workout });
        return Ok(workout);
    }
}

public record WorkoutRequest(
    string? Day, string Activity, DateTime? StartTime, DateTime? EndTime,
    int? Calories, int? Distance, string? Intensity, string? Label);
