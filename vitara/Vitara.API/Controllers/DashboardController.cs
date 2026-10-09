using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Health;

namespace Vitara.API.Controllers;

[ApiController, Route("api/dashboard")]
public class DashboardController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var today   = LocalTime.Today;
        var weekAgo = today.AddDays(-7);

        var profile    = await repo.GetProfileAsync();
        // One night per day. Oura also returns naps and short segments, and taking whichever
        // session came last showed an afternoon nap as last night's sleep -- with the nap's HRV,
        // resting heart rate and skin temperature beside it. See SleepNights.
        var sleep      = SleepNights.MainPerDay(await repo.GetSleepAsync(weekAgo, today));
        var readiness  = await repo.GetReadinessAsync(weekAgo, today);
        var activity   = await repo.GetActivityAsync(weekAgo, today);
        var stress     = await repo.GetStressAsync(weekAgo, today);
        var spo2       = await repo.GetSpo2Async(weekAgo, today);

        // Slow movers get their own windows.
        //
        // A week is the right look-back for sleep and steps and the wrong one for these:
        // Oura publishes cardiovascular age and VO2 max every few weeks, and resilience
        // is built from weeks rather than days. Asking for seven days of them returns
        // nothing most of the time, and the dashboard then reported "needs more wear" to
        // someone who had a perfectly good reading a fortnight old. The Body tab, which
        // asks for thirty days, was showing that reading at the same moment.
        //
        // Each carries its own day out, so an old reading is shown as old rather than as
        // today's -- the rule the rest of this payload has followed since it was caught
        // presenting four-day-old vitals as current.
        var resilience = await repo.GetResilienceAsync(today.AddDays(-30), today);
        var cvAge      = await repo.GetCardiovascularAgeAsync(today.AddDays(-90), today);
        var vo2        = await repo.GetVo2MaxAsync(today.AddDays(-90), today);
        var workouts   = await repo.GetWorkoutsAsync(weekAgo, today);
        var heartRate  = await repo.GetHeartRateAsync(DateTime.UtcNow.AddHours(-24), DateTime.UtcNow);

        // Samples come back oldest-first. Downsample, then take from the END: the old
        // `.Take(120)` kept the OLDEST 120 points, so as soon as a day held more than
        // ~720 raw samples the chart silently stopped at some hours-old moment and the
        // newest readings — the ones that make it a "current" vital — were the first
        // thing discarded. Oura's own sampling stays under that; a HealthKit workout
        // feed does not.
        var hrSeries = heartRate.Where((_, i) => i % 6 == 0).TakeLast(120).ToList();
        var latestHr = heartRate.LastOrDefault();

        var todaySleep = sleep.LastOrDefault();
        var todayRead  = readiness.LastOrDefault();
        var todayAct   = activity.LastOrDefault();
        var todayStress = stress.LastOrDefault();
        var todayRes   = resilience.LastOrDefault();
        var todaySpo2  = spo2.LastOrDefault();
        var latestCvAge = cvAge.LastOrDefault();
        var latestVo2  = vo2.LastOrDefault();

        // Each block below is that metric's MOST RECENT record within the window, which
        // may be days old — Oura syncs once daily, and a night the ring wasn't worn (or
        // didn't sync) leaves a gap. Carrying only a top-level `date` meant a consumer
        // couldn't tell last night's sleep from last week's and would report stale
        // numbers as current, so every block states its own day and age.
        return Ok(new
        {
            date = today.ToString("yyyy-MM-dd"),
            profile = profile is not null ? new { Age = profile.CurrentAge, profile.Weight, profile.Height, profile.BiologicalSex, profile.Name } : null,
            sleep = todaySleep is null ? null : new
            {
                day = todaySleep.Day.ToString("yyyy-MM-dd"),
                daysAgo = today.DayNumber - todaySleep.Day.DayNumber,
                score = todaySleep.Score,
                totalMinutes = todaySleep.TotalSleepMinutes,
                deepMinutes = todaySleep.DeepMinutes,
                remMinutes = todaySleep.RemMinutes,
                lightMinutes = todaySleep.LightMinutes,
                efficiency = Math.Round(todaySleep.Efficiency * 100, 0),
                hrv = todaySleep.AvgHrv.HasValue ? Math.Round(todaySleep.AvgHrv.Value, 0) : (double?)null,
                lowestHr = todaySleep.LowestHr.HasValue ? Math.Round(todaySleep.LowestHr.Value, 0) : (double?)null,
                breathingRate = todaySleep.AvgBreathingRate,
                spo2 = todaySleep.AvgSpo2,
                skinTemp = todaySleep.SkinTempDeviation,
            },
            readiness = todayRead is null ? null : new
            {
                day = todayRead.Day.ToString("yyyy-MM-dd"),
                daysAgo = today.DayNumber - todayRead.Day.DayNumber,
                score = todayRead.Score,
                level = todayRead.Level,
                // From the night, not from the readiness contributors. The two used to be
                // confused here, which is how a perfect contributor score of 100 reached
                // the dashboard as a resting pulse of 100 bpm.
                restingHr = todaySleep?.LowestHr is { } bpm ? Math.Round(bpm, 0) : (double?)null,
                restingHrContributor = todayRead.RestingHrContributor,
                hrvBalance = todayRead.HrvBalance,
                recoveryIndex = todayRead.RecoveryIndex,
                activityBalance = todayRead.ActivityBalance,
                sleepBalance = todayRead.SleepBalance,
                // The night's reading in degrees, not the readiness contributor. Same
                // mistake as restingHr, one field along.
                tempDeviation = todaySleep?.SkinTempDeviation,
                tempContributor = todayRead.TemperatureContributor,
            },
            activity = todayAct is null ? null : new
            {
                day = todayAct.Day.ToString("yyyy-MM-dd"),
                daysAgo = today.DayNumber - todayAct.Day.DayNumber,
                score = todayAct.Score,
                steps = todayAct.Steps,
                activeCalories = todayAct.ActiveCalories,
                totalCalories = todayAct.TotalCalories,
                highMinutes = todayAct.HighActivityMinutes,
                mediumMinutes = todayAct.MediumActivityMinutes,
                lowMinutes = todayAct.LowActivityMinutes,
                distance = todayAct.EquivalentWalkingDistance,
            },
            stress = todayStress is null ? null : new
            {
                day = todayStress.Day.ToString("yyyy-MM-dd"),
                daysAgo = today.DayNumber - todayStress.Day.DayNumber,
                summary = todayStress.DaySummary,
                stressMinutes = todayStress.StressHighSeconds.HasValue ? todayStress.StressHighSeconds.Value / 60 : (int?)null,
                recoveryMinutes = todayStress.RecoveryHighSeconds.HasValue ? todayStress.RecoveryHighSeconds.Value / 60 : (int?)null,
            },
            resilience = todayRes is null ? null : new
            {
                day = todayRes.Day.ToString("yyyy-MM-dd"),
                daysAgo = today.DayNumber - todayRes.Day.DayNumber,
                level = todayRes.Level,
                sleepRecovery = todayRes.SleepRecovery,
                daytimeRecovery = todayRes.DaytimeRecovery,
                stressScore = todayRes.Stress,
            },
            spo2Data = todaySpo2 is null ? null : new
            {
                day = todaySpo2.Day.ToString("yyyy-MM-dd"),
                daysAgo = today.DayNumber - todaySpo2.Day.DayNumber,
                average = todaySpo2.Spo2Average,
                breathingDisturbance = todaySpo2.BreathingDisturbanceIndex,
            },
            cardiovascularAge = latestCvAge?.VascularAge,
            cardiovascularAgeDay = latestCvAge?.Day.ToString("yyyy-MM-dd"),
            cardiovascularAgeDaysAgo = latestCvAge is null ? (int?)null : today.DayNumber - latestCvAge.Day.DayNumber,
            vo2Max = latestVo2?.Vo2Max,
            vo2MaxDay = latestVo2?.Day.ToString("yyyy-MM-dd"),
            vo2MaxDaysAgo = latestVo2 is null ? (int?)null : today.DayNumber - latestVo2.Day.DayNumber,
            weeklyAvg = new
            {
                hrv = Math.Round(sleep.Where(s => s.AvgHrv.HasValue).Select(s => s.AvgHrv!.Value).DefaultIfEmpty(0).Average(), 0),
                rhr = Math.Round(sleep.Where(s => s.LowestHr.HasValue).Select(s => s.LowestHr!.Value).DefaultIfEmpty(0).Average(), 0),
                sleepScore = Math.Round(sleep.Where(s => s.Score.HasValue).Select(s => (double)s.Score!.Value).DefaultIfEmpty(0).Average(), 0),
                readinessScore = Math.Round(readiness.Where(r => r.Score.HasValue).Select(r => (double)r.Score!.Value).DefaultIfEmpty(0).Average(), 0),
                steps = Math.Round(activity.Select(a => (double)a.Steps).DefaultIfEmpty(0).Average(), 0),
                activityScore = Math.Round(activity.Where(a => a.Score.HasValue).Select(a => (double)a.Score!.Value).DefaultIfEmpty(0).Average(), 0),
            },
            recentWorkouts = workouts.Take(3).Select(w => new { w.Activity, w.Calories, w.Distance, w.Intensity, w.StartTime }),
            // The single most recent reading, which is the only genuinely "right now"
            // number on this whole endpoint — everything else is a daily rollup Oura
            // computes once. Carries its own timestamp because a ring that hasn't synced
            // since this morning must not have its last reading shown as current.
            latestHeartRate = latestHr is null ? null : new { latestHr.Timestamp, latestHr.Bpm },
            heartRateSamples = hrSeries.Select(h => new { h.Timestamp, h.Bpm }),
        });
    }
}
