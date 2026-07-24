using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;

namespace Vitara.API.Controllers;

// Direct ingest from the iOS MaayaCompanion app's Apple HealthKit data.
// Auth mirrors San's /api/context/push: an X-Device-Key header validated against
// the DEVICE_API_KEY env var — use the same key value for both San and Vitara so
// one device configuration serves both.
[ApiController, Route("api/healthkit")]
public class HealthKitController(IVitaraRepository repo) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest([FromBody] HealthKitPayload req)
    {
        var expectedKey = Environment.GetEnvironmentVariable("DEVICE_API_KEY") ?? "changeme";
        var deviceKey = Request.Headers["X-Device-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(deviceKey) || deviceKey != expectedKey)
            return Unauthorized(new { error = "Invalid or missing X-Device-Key header." });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var applied = new List<string>();

        // ── Activity (steps + active calories) — merge into today's row, preserving any Oura fields ──
        if (req.Steps.HasValue || req.ActiveCalories.HasValue)
        {
            var existing = (await repo.GetActivityAsync(today, today)).FirstOrDefault();
            var activity = existing ?? new DailyActivity { Id = today.ToString("yyyy-MM-dd"), Day = today };
            if (req.Steps.HasValue) activity.Steps = req.Steps.Value;
            if (req.ActiveCalories.HasValue) activity.ActiveCalories = req.ActiveCalories.Value;
            await repo.UpsertActivityAsync(new[] { activity });
            applied.Add("activity");
        }

        // ── Heart rate (latest reading) ──
        if (req.HeartRate.HasValue)
        {
            await repo.UpsertHeartRateAsync(new[]
            {
                new HeartRateSample
                {
                    Timestamp = req.Timestamp?.ToUniversalTime() ?? DateTime.UtcNow,
                    Bpm = req.HeartRate.Value,
                    Source = "apple_health",
                }
            });
            applied.Add("heartRate");
        }

        // ── Sleep — needs real bedtime start/end from HealthKit ──
        if (req.SleepHours is > 0 && req.SleepStart.HasValue && req.SleepEnd.HasValue)
        {
            var sleepDay = DateOnly.FromDateTime(req.SleepEnd.Value.ToLocalTime());
            var session = new SleepSession
            {
                Id = $"applehealth-{sleepDay:yyyy-MM-dd}",
                Day = sleepDay,
                BedtimeStart = req.SleepStart.Value.ToUniversalTime(),
                BedtimeEnd = req.SleepEnd.Value.ToUniversalTime(),
                TotalSleepMinutes = (int)Math.Round(req.SleepHours.Value * 60),
            };
            await repo.UpsertSleepAsync(new[] { session });
            applied.Add("sleep");
        }

        // ── Weight (latest reading, tagged to today) — richer iOS payload ──
        if (req.WeightKg is > 0)
        {
            var wDay = DateOnly.FromDateTime(DateTime.UtcNow);
            await repo.UpsertWeighInAsync(new WeighIn
            {
                Id = wDay.ToString("yyyy-MM-dd"),
                Day = wDay,
                WeightKg = req.WeightKg.Value,
            });
            applied.Add("weight");
        }

        // ── Workouts (last week) — upsert by start time ──
        if (req.Workouts is { Count: > 0 })
        {
            var workouts = req.Workouts.Select(w => new Workout
            {
                Id = $"applehealth-{w.Start.ToUniversalTime():yyyyMMddHHmmss}",
                Day = DateOnly.FromDateTime(w.Start.ToLocalTime()),
                Activity = w.Activity,
                StartTime = w.Start.ToUniversalTime(),
                EndTime = w.End.ToUniversalTime(),
                Calories = w.Calories,
                Distance = w.DistanceMeters,
                Intensity = w.Intensity,
                Source = "apple_health",
            });
            await repo.UpsertWorkoutsAsync(workouts);
            applied.Add("workouts");
        }

        // ── Multi-day activity backfill (steps + active calories per day) ──
        if (req.DailyActivity is { Count: > 0 })
        {
            foreach (var d in req.DailyActivity)
            {
                if (!DateOnly.TryParseExact(d.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    continue;
                var existing = (await repo.GetActivityAsync(day, day)).FirstOrDefault();
                var activity = existing ?? new DailyActivity { Id = day.ToString("yyyy-MM-dd"), Day = day };
                if (d.Steps.HasValue) activity.Steps = d.Steps.Value;
                if (d.ActiveCalories.HasValue) activity.ActiveCalories = d.ActiveCalories.Value;
                await repo.UpsertActivityAsync(new[] { activity });
            }
            applied.Add("dailyActivity");
        }

        return Ok(new { received = true, applied });
    }
}

public record HealthKitPayload(
    int? Steps,
    int? HeartRate,
    int? ActiveCalories,
    double? SleepHours,
    DateTime? SleepStart,
    DateTime? SleepEnd,
    DateTime? Timestamp,
    // Richer fields from the iOS app's full Vitara sync (all optional — the
    // original context-push snapshot omits them and still works unchanged).
    double? WeightKg = null,
    List<HealthKitWorkout>? Workouts = null,
    List<HealthKitDailyActivity>? DailyActivity = null);

public record HealthKitWorkout(
    string Activity,
    int? Calories,
    int? DistanceMeters,
    DateTime Start,
    DateTime End,
    string? Intensity);

public record HealthKitDailyActivity(
    string Day,
    int? Steps,
    int? ActiveCalories);
