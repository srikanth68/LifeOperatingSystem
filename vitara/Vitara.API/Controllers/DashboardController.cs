using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;

namespace Vitara.API.Controllers;

[ApiController, Route("api/dashboard")]
public class DashboardController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var today   = DateOnly.FromDateTime(DateTime.UtcNow);
        var weekAgo = today.AddDays(-7);

        var profile    = await repo.GetProfileAsync();
        var sleep      = await repo.GetSleepAsync(weekAgo, today);
        var readiness  = await repo.GetReadinessAsync(weekAgo, today);
        var activity   = await repo.GetActivityAsync(weekAgo, today);
        var stress     = await repo.GetStressAsync(weekAgo, today);
        var resilience = await repo.GetResilienceAsync(weekAgo, today);
        var spo2       = await repo.GetSpo2Async(weekAgo, today);
        var cvAge      = await repo.GetCardiovascularAgeAsync(weekAgo, today);
        var vo2        = await repo.GetVo2MaxAsync(today.AddDays(-90), today);
        var workouts   = await repo.GetWorkoutsAsync(weekAgo, today);
        var heartRate  = await repo.GetHeartRateAsync(DateTime.UtcNow.AddHours(-24), DateTime.UtcNow);

        var todaySleep = sleep.LastOrDefault();
        var todayRead  = readiness.LastOrDefault();
        var todayAct   = activity.LastOrDefault();
        var todayStress = stress.LastOrDefault();
        var todayRes   = resilience.LastOrDefault();
        var todaySpo2  = spo2.LastOrDefault();
        var latestCvAge = cvAge.LastOrDefault();
        var latestVo2  = vo2.LastOrDefault();

        return Ok(new
        {
            date = today.ToString("yyyy-MM-dd"),
            profile = profile is not null ? new { profile.Age, profile.Weight, profile.Height, profile.BiologicalSex } : null,
            sleep = todaySleep is null ? null : new
            {
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
                score = todayRead.Score,
                level = todayRead.Level,
                restingHr = todayRead.RestingHeartRate,
                hrvBalance = todayRead.HrvBalance,
                recoveryIndex = todayRead.RecoveryIndex,
                activityBalance = todayRead.ActivityBalance,
                sleepBalance = todayRead.SleepBalance,
                tempDeviation = todayRead.TemperatureDeviation,
            },
            activity = todayAct is null ? null : new
            {
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
                summary = todayStress.DaySummary,
                stressMinutes = todayStress.StressHighSeconds.HasValue ? todayStress.StressHighSeconds.Value / 60 : (int?)null,
                recoveryMinutes = todayStress.RecoveryHighSeconds.HasValue ? todayStress.RecoveryHighSeconds.Value / 60 : (int?)null,
            },
            resilience = todayRes is null ? null : new
            {
                level = todayRes.Level,
                sleepRecovery = todayRes.SleepRecovery,
                daytimeRecovery = todayRes.DaytimeRecovery,
                stressScore = todayRes.Stress,
            },
            spo2Data = todaySpo2 is null ? null : new
            {
                average = todaySpo2.Spo2Average,
                breathingDisturbance = todaySpo2.BreathingDisturbanceIndex,
            },
            cardiovascularAge = latestCvAge?.VascularAge,
            vo2Max = latestVo2?.Vo2Max,
            weeklyAvg = new
            {
                hrv = Math.Round(sleep.Where(s => s.AvgHrv.HasValue).Select(s => s.AvgHrv!.Value).DefaultIfEmpty(0).Average(), 0),
                rhr = Math.Round(readiness.Where(r => r.RestingHeartRate.HasValue).Select(r => (double)r.RestingHeartRate!.Value).DefaultIfEmpty(0).Average(), 0),
                sleepScore = Math.Round(sleep.Where(s => s.Score.HasValue).Select(s => (double)s.Score!.Value).DefaultIfEmpty(0).Average(), 0),
                readinessScore = Math.Round(readiness.Where(r => r.Score.HasValue).Select(r => (double)r.Score!.Value).DefaultIfEmpty(0).Average(), 0),
                steps = Math.Round(activity.Select(a => (double)a.Steps).DefaultIfEmpty(0).Average(), 0),
                activityScore = Math.Round(activity.Where(a => a.Score.HasValue).Select(a => (double)a.Score!.Value).DefaultIfEmpty(0).Average(), 0),
            },
            recentWorkouts = workouts.Take(3).Select(w => new { w.Activity, w.Calories, w.Distance, w.Intensity, w.StartTime }),
            heartRateSamples = heartRate.Where((_, i) => i % 6 == 0).Take(120).Select(h => new { h.Timestamp, h.Bpm }),
        });
    }
}
