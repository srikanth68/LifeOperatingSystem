using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;

namespace Vitara.API.Controllers;

[ApiController, Route("api/workouts")]
public class WorkoutsController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 30)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(await repo.GetWorkoutsAsync(to.AddDays(-days), to));
    }

    [HttpPost]
    public async Task<IActionResult> Log([FromBody] WorkoutRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Activity)) return BadRequest("Activity is required.");
        var day = string.IsNullOrWhiteSpace(req.Day)
            ? DateOnly.FromDateTime(DateTime.UtcNow)
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
