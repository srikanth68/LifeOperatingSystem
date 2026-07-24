using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Vitara.API.Controllers;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;

// Controllers return anonymous objects which serialize PascalCase by default.
// Use case-insensitive JSON to match both camelCase and PascalCase.

namespace Vitara.Tests;

public class FakeRepo : IVitaraRepository
{
    public UserProfile? Profile { get; set; }
    public List<SleepSession> SleepData { get; set; } = new();
    public List<DailyReadiness> ReadinessData { get; set; } = new();
    public List<DailyActivity> ActivityData { get; set; } = new();
    public List<DailyStress> StressData { get; set; } = new();
    public List<DailyResilience> ResilienceData { get; set; } = new();
    public List<DailyCardiovascularAge> CvAgeData { get; set; } = new();
    public List<DailySpo2> Spo2Data { get; set; } = new();
    public List<HeartRateSample> HeartRateData { get; set; } = new();
    public List<Vo2MaxRecord> Vo2Data { get; set; } = new();
    public List<Workout> WorkoutData { get; set; } = new();
    public OuraToken? Token { get; set; }

    public Task<OuraToken?> GetTokenAsync() => Task.FromResult(Token);
    public Task SaveTokenAsync(OuraToken t) { Token = t; return Task.CompletedTask; }
    public Task DeleteTokenAsync() { Token = null; return Task.CompletedTask; }
    public Task<UserProfile?> GetProfileAsync() => Task.FromResult(Profile);
    public Task SaveProfileAsync(UserProfile p) { Profile = p; return Task.CompletedTask; }

    public Task UpsertSleepAsync(IEnumerable<SleepSession> s) { SleepData.AddRange(s); return Task.CompletedTask; }
    public Task<List<SleepSession>> GetSleepAsync(DateOnly from, DateOnly to) =>
        Task.FromResult(SleepData.Where(s => s.Day >= from && s.Day <= to).OrderBy(s => s.Day).ToList());

    public Task UpsertReadinessAsync(IEnumerable<DailyReadiness> r) { ReadinessData.AddRange(r); return Task.CompletedTask; }
    public Task<List<DailyReadiness>> GetReadinessAsync(DateOnly from, DateOnly to) =>
        Task.FromResult(ReadinessData.Where(r => r.Day >= from && r.Day <= to).OrderBy(r => r.Day).ToList());

    public Task UpsertActivityAsync(IEnumerable<DailyActivity> a) { ActivityData.AddRange(a); return Task.CompletedTask; }
    public Task<List<DailyActivity>> GetActivityAsync(DateOnly from, DateOnly to) =>
        Task.FromResult(ActivityData.Where(a => a.Day >= from && a.Day <= to).OrderBy(a => a.Day).ToList());

    public Task UpsertStressAsync(IEnumerable<DailyStress> s) { StressData.AddRange(s); return Task.CompletedTask; }
    public Task<List<DailyStress>> GetStressAsync(DateOnly from, DateOnly to) =>
        Task.FromResult(StressData.Where(s => s.Day >= from && s.Day <= to).OrderBy(s => s.Day).ToList());

    public Task UpsertResilienceAsync(IEnumerable<DailyResilience> r) { ResilienceData.AddRange(r); return Task.CompletedTask; }
    public Task<List<DailyResilience>> GetResilienceAsync(DateOnly from, DateOnly to) =>
        Task.FromResult(ResilienceData.Where(r => r.Day >= from && r.Day <= to).OrderBy(r => r.Day).ToList());

    public Task UpsertCardiovascularAgeAsync(IEnumerable<DailyCardiovascularAge> c) { CvAgeData.AddRange(c); return Task.CompletedTask; }
    public Task<List<DailyCardiovascularAge>> GetCardiovascularAgeAsync(DateOnly from, DateOnly to) =>
        Task.FromResult(CvAgeData.Where(c => c.Day >= from && c.Day <= to).OrderBy(c => c.Day).ToList());

    public Task UpsertSpo2Async(IEnumerable<DailySpo2> s) { Spo2Data.AddRange(s); return Task.CompletedTask; }
    public Task<List<DailySpo2>> GetSpo2Async(DateOnly from, DateOnly to) =>
        Task.FromResult(Spo2Data.Where(s => s.Day >= from && s.Day <= to).OrderBy(s => s.Day).ToList());

    public Task UpsertHeartRateAsync(IEnumerable<HeartRateSample> s) { HeartRateData.AddRange(s); return Task.CompletedTask; }
    public Task<List<HeartRateSample>> GetHeartRateAsync(DateTime from, DateTime to) =>
        Task.FromResult(HeartRateData.Where(h => h.Timestamp >= from && h.Timestamp <= to).OrderBy(h => h.Timestamp).ToList());

    public Task UpsertVo2MaxAsync(IEnumerable<Vo2MaxRecord> v) { Vo2Data.AddRange(v); return Task.CompletedTask; }
    public Task<List<Vo2MaxRecord>> GetVo2MaxAsync(DateOnly from, DateOnly to) =>
        Task.FromResult(Vo2Data.Where(v => v.Day >= from && v.Day <= to).OrderBy(v => v.Day).ToList());

    public Task UpsertWorkoutsAsync(IEnumerable<Workout> w) { WorkoutData.AddRange(w); return Task.CompletedTask; }
    public Task<List<Workout>> GetWorkoutsAsync(DateOnly from, DateOnly to) =>
        Task.FromResult(WorkoutData.Where(w => w.Day >= from && w.Day <= to).OrderByDescending(w => w.StartTime).ToList());

    public List<DailyNutrition> NutritionData { get; } = [];
    public Task UpsertNutritionAsync(IEnumerable<DailyNutrition> entries) { NutritionData.AddRange(entries); return Task.CompletedTask; }
    public Task<List<DailyNutrition>> GetNutritionAsync(DateOnly from, DateOnly to) =>
        Task.FromResult(NutritionData.Where(n => n.Day >= from && n.Day <= to).OrderBy(n => n.Day).ToList());

    public List<MealEntry> MealData { get; } = [];
    public Task<MealEntry> AddMealAsync(MealEntry meal) { MealData.Add(meal); return Task.FromResult(meal); }
    public Task<MealEntry?> GetMealAsync(Guid id) => Task.FromResult(MealData.FirstOrDefault(m => m.Id == id));
    public Task<List<MealEntry>> GetMealsAsync(DateOnly day) => Task.FromResult(MealData.Where(m => m.Day == day).ToList());
    public Task<MealEntry?> UpdateMealAsync(MealEntry meal) => Task.FromResult<MealEntry?>(meal);
    public Task<bool> DeleteMealAsync(Guid id) => Task.FromResult(true);

    public List<WeighIn> WeighInData { get; } = [];
    public Task UpsertWeighInAsync(WeighIn weighIn) { WeighInData.RemoveAll(w => w.Id == weighIn.Id); WeighInData.Add(weighIn); return Task.CompletedTask; }
    public Task<List<WeighIn>> GetWeighInsAsync(DateOnly from, DateOnly to) =>
        Task.FromResult(WeighInData.Where(w => w.Day >= from && w.Day <= to).OrderBy(w => w.Day).ToList());

    public Task<DateOnly?> GetLatestDayAsync() => Task.FromResult<DateOnly?>(null);
}

// ── Dashboard Controller ──

public class DashboardControllerTests
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static JsonElement GetJson(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, Opts);
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public async Task Dashboard_ReturnsOk_WithEmptyData()
    {
        var ctrl = new DashboardController(new FakeRepo());
        var result = await ctrl.Get();
        var json = GetJson(result);

        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"), json.GetProperty("date").GetString());
        Assert.True(json.GetProperty("profile").ValueKind == JsonValueKind.Null);
        Assert.True(json.GetProperty("sleep").ValueKind == JsonValueKind.Null);
        Assert.True(json.GetProperty("readiness").ValueKind == JsonValueKind.Null);
        Assert.True(json.GetProperty("activity").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Dashboard_ReturnsProfile_WhenPresent()
    {
        var repo = new FakeRepo { Profile = new UserProfile { Id = "p", Age = 38, BiologicalSex = "male", Weight = 88, Height = 1.7 } };
        var ctrl = new DashboardController(repo);
        var json = GetJson(await ctrl.Get());

        var profile = json.GetProperty("profile");
        Assert.Equal(38, profile.GetProperty("age").GetInt32());
        Assert.Equal("male", profile.GetProperty("biologicalSex").GetString());
    }

    [Fact]
    public async Task Dashboard_ReturnsSleepData_ForToday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new FakeRepo();
        repo.SleepData.Add(new SleepSession
        {
            Id = "ds1", Day = today,
            BedtimeStart = today.AddDays(-1).ToDateTime(new TimeOnly(23, 0)),
            BedtimeEnd = today.ToDateTime(new TimeOnly(7, 0)),
            TotalSleepMinutes = 400, DeepMinutes = 80, RemMinutes = 100, LightMinutes = 200, AwakeMinutes = 20,
            Score = 85, AvgHrv = 32.0, LowestHr = 55.0
        });

        var json = GetJson(await new DashboardController(repo).Get());
        var sleep = json.GetProperty("sleep");
        Assert.Equal(400, sleep.GetProperty("totalMinutes").GetInt32());
        Assert.Equal(80, sleep.GetProperty("deepMinutes").GetInt32());
    }

    [Fact]
    public async Task Dashboard_WeeklyAvg_ComputesCorrectly()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new FakeRepo();
        for (int i = 0; i < 7; i++)
        {
            var day = today.AddDays(-i);
            repo.ActivityData.Add(new DailyActivity { Id = $"wa{i}", Day = day, Steps = 10000, Score = 80 });
        }

        var json = GetJson(await new DashboardController(repo).Get());
        var avg = json.GetProperty("weeklyAvg");
        Assert.Equal(10000, avg.GetProperty("steps").GetDouble());
        Assert.Equal(80, avg.GetProperty("activityScore").GetDouble());
    }

    [Fact]
    public async Task Dashboard_RecentWorkouts_LimitedTo3()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new FakeRepo();
        for (int i = 0; i < 5; i++)
            repo.WorkoutData.Add(new Workout { Id = $"dw{i}", Day = today, Activity = "running", StartTime = DateTime.UtcNow.AddHours(-i) });

        var json = GetJson(await new DashboardController(repo).Get());
        Assert.Equal(3, json.GetProperty("recentWorkouts").GetArrayLength());
    }

    [Fact]
    public async Task Dashboard_StressMinutes_ConvertedFromSeconds()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new FakeRepo();
        repo.StressData.Add(new DailyStress { Id = "dst", Day = today, StressHighSeconds = 3600, RecoveryHighSeconds = 1800 });

        var json = GetJson(await new DashboardController(repo).Get());
        var stress = json.GetProperty("stress");
        Assert.Equal(60, stress.GetProperty("stressMinutes").GetInt32());
        Assert.Equal(30, stress.GetProperty("recoveryMinutes").GetInt32());
    }
}

// ── Profile Controller ──

public class ProfileControllerTests
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task Profile_ReturnsNotSynced_WhenEmpty()
    {
        var ctrl = new ProfileController(new FakeRepo());
        var ok = Assert.IsType<OkObjectResult>(await ctrl.Get());
        var json = JsonSerializer.Serialize(ok.Value, Opts);
        Assert.Contains("\"synced\":false", json);
    }

    [Fact]
    public async Task Profile_ReturnsSynced_WithData()
    {
        var repo = new FakeRepo { Profile = new UserProfile { Id = "p", Age = 25 } };
        var ok = Assert.IsType<OkObjectResult>(await new ProfileController(repo).Get());
        var json = JsonSerializer.Serialize(ok.Value, Opts);
        Assert.Contains("\"synced\":true", json);
        Assert.Contains("\"age\":25", json);
    }
}

// ── Sleep Controller ──

public class SleepControllerTests
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    [Fact]
    public async Task Sleep_ReturnsEmptyList_WhenNoData()
    {
        var ctrl = new SleepController(new FakeRepo());
        var ok = Assert.IsType<OkObjectResult>(await ctrl.Get(7));
        var list = Assert.IsAssignableFrom<List<SleepSession>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Sleep_Summary_ReturnsCountZero_WhenNoData()
    {
        var ok = Assert.IsType<OkObjectResult>(await new SleepController(new FakeRepo()).Summary(7));
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"count\":0", json);
    }

    [Fact]
    public async Task Sleep_Summary_ComputesAverages()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new FakeRepo();
        repo.SleepData.AddRange(new[]
        {
            MakeSleep("ss1", today, score: 80, hrv: 30),
            MakeSleep("ss2", today.AddDays(-1), score: 90, hrv: 40),
        });

        var ok = Assert.IsType<OkObjectResult>(await new SleepController(repo).Summary(7));
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;
        Assert.Equal(2, json.GetProperty("count").GetInt32());
        Assert.Equal(85, json.GetProperty("avgScore").GetDouble());
        Assert.Equal(35, json.GetProperty("avgHrv").GetDouble());
    }

    private static SleepSession MakeSleep(string id, DateOnly day, int score = 80, double hrv = 35) => new()
    {
        Id = id, Day = day,
        BedtimeStart = day.AddDays(-1).ToDateTime(new TimeOnly(23, 0)),
        BedtimeEnd = day.ToDateTime(new TimeOnly(7, 0)),
        TotalSleepMinutes = 420, DeepMinutes = 60, RemMinutes = 90, LightMinutes = 180, AwakeMinutes = 30,
        Score = score, AvgHrv = hrv,
    };
}

// ── Activity Controller ──

public class ActivityControllerTests
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    [Fact]
    public async Task Activity_ReturnsData_WithinRange()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new FakeRepo();
        repo.ActivityData.Add(new DailyActivity { Id = "ac1", Day = today, Steps = 12000, Score = 90 });

        var ok = Assert.IsType<OkObjectResult>(await new ActivityController(repo).Get(7));
        var list = Assert.IsAssignableFrom<List<DailyActivity>>(ok.Value);
        Assert.Single(list);
        Assert.Equal(12000, list[0].Steps);
    }

    [Fact]
    public async Task Activity_Summary_ComputesAverages()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new FakeRepo();
        repo.ActivityData.AddRange(new[]
        {
            new DailyActivity { Id = "as1", Day = today, Steps = 8000, ActiveCalories = 400, Score = 70 },
            new DailyActivity { Id = "as2", Day = today.AddDays(-1), Steps = 12000, ActiveCalories = 600, Score = 90 },
        });

        var ok = Assert.IsType<OkObjectResult>(await new ActivityController(repo).Summary(7));
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;
        Assert.Equal(2, json.GetProperty("count").GetInt32());
        Assert.Equal(10000, json.GetProperty("avgSteps").GetDouble());
    }
}

// ── Readiness Controller ──

public class ReadinessControllerTests
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    [Fact]
    public async Task Readiness_Summary_GroupsLevels()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new FakeRepo();
        repo.ReadinessData.AddRange(new[]
        {
            new DailyReadiness { Id = "rs1", Day = today, Score = 85, Level = "optimal", HrvBalance = 90, RestingHeartRate = 60 },
            new DailyReadiness { Id = "rs2", Day = today.AddDays(-1), Score = 70, Level = "good", HrvBalance = 80, RestingHeartRate = 65 },
            new DailyReadiness { Id = "rs3", Day = today.AddDays(-2), Score = 88, Level = "optimal", HrvBalance = 95, RestingHeartRate = 58 },
        });

        var ok = Assert.IsType<OkObjectResult>(await new ReadinessController(repo).Summary(7));
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;
        Assert.Equal(3, json.GetProperty("count").GetInt32());

        var levels = json.GetProperty("levelCounts");
        Assert.Equal(2, levels.GetProperty("optimal").GetInt32());
        Assert.Equal(1, levels.GetProperty("good").GetInt32());
    }
}

// ── BioAge Controller ──

public class BioAgeControllerTests
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task BioAge_ReturnsInsufficient_WhenNoData()
    {
        var ctrl = new BioAgeController(new FakeRepo());
        var ok = Assert.IsType<OkObjectResult>(await ctrl.Get());
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;
        Assert.Equal("insufficient", json.GetProperty("dataQuality").GetString());
    }

    [Fact]
    public async Task BioAge_ComputesBioAge_WithSufficientData()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new FakeRepo
        {
            Profile = new UserProfile { Id = "ba", Age = 38 }
        };

        for (int i = 0; i < 14; i++)
        {
            var day = today.AddDays(-i);
            repo.SleepData.Add(new SleepSession
            {
                Id = $"ba-s{i}", Day = day,
                BedtimeStart = day.AddDays(-1).ToDateTime(new TimeOnly(23, 0)),
                BedtimeEnd = day.ToDateTime(new TimeOnly(7, 0)),
                TotalSleepMinutes = 420, DeepMinutes = 70, RemMinutes = 90, LightMinutes = 180, AwakeMinutes = 30,
                Score = 82, AvgHrv = 35.0,
            });
            repo.ReadinessData.Add(new DailyReadiness
            {
                Id = $"ba-r{i}", Day = day, Score = 80, RestingHeartRate = 65, HrvBalance = 85
            });
        }

        var ok = Assert.IsType<OkObjectResult>(await new BioAgeController(repo).Get());
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;

        Assert.Equal("good", json.GetProperty("dataQuality").GetString());
        Assert.Equal(38, json.GetProperty("chronologicalAge").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, json.GetProperty("bioAge").ValueKind);
        Assert.Equal("oura", json.GetProperty("ageSource").GetString());
    }

    [Fact]
    public async Task BioAge_UsesCardiovascularAge_WhenPresent()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new FakeRepo { Profile = new UserProfile { Id = "cv", Age = 40 } };

        for (int i = 0; i < 5; i++)
        {
            var day = today.AddDays(-i);
            repo.SleepData.Add(new SleepSession
            {
                Id = $"cv-s{i}", Day = day,
                BedtimeStart = day.AddDays(-1).ToDateTime(new TimeOnly(23, 0)),
                BedtimeEnd = day.ToDateTime(new TimeOnly(7, 0)),
                TotalSleepMinutes = 420, DeepMinutes = 70, RemMinutes = 90, LightMinutes = 180, AwakeMinutes = 30,
                Score = 85, AvgHrv = 45.0,
            });
            repo.ReadinessData.Add(new DailyReadiness { Id = $"cv-r{i}", Day = day, Score = 85, RestingHeartRate = 60, HrvBalance = 90 });
        }
        repo.CvAgeData.Add(new DailyCardiovascularAge { Id = "cv1", Day = today, VascularAge = 35.0 });

        var ok = Assert.IsType<OkObjectResult>(await new BioAgeController(repo).Get());
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;
        Assert.Equal(35.0, json.GetProperty("cardiovascularAge").GetDouble());
        Assert.True(json.GetProperty("bioAge").GetDouble() < 40);
    }

    [Fact]
    public async Task BioAge_ClampsDelta_ToMaxRange()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new FakeRepo { Profile = new UserProfile { Id = "clamp", Age = 30 } };

        for (int i = 0; i < 5; i++)
        {
            var day = today.AddDays(-i);
            repo.SleepData.Add(new SleepSession
            {
                Id = $"cl-s{i}", Day = day,
                BedtimeStart = day.AddDays(-1).ToDateTime(new TimeOnly(23, 0)),
                BedtimeEnd = day.ToDateTime(new TimeOnly(7, 0)),
                TotalSleepMinutes = 420, DeepMinutes = 70, RemMinutes = 90, LightMinutes = 180, AwakeMinutes = 30,
                Score = 10, AvgHrv = 5.0,
            });
            repo.ReadinessData.Add(new DailyReadiness { Id = $"cl-r{i}", Day = day, Score = 10, RestingHeartRate = 120 });
        }

        var ok = Assert.IsType<OkObjectResult>(await new BioAgeController(repo).Get());
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;
        var bioAge = json.GetProperty("bioAge").GetDouble();
        Assert.True(bioAge <= 45.0, $"BioAge {bioAge} should be clamped to chronoAge + 15");
    }
}

// ── Protocols Controller ──

public class ProtocolsControllerTests
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task Protocols_Returns5Protocols_Always()
    {
        var ctrl = new ProtocolsController(new FakeRepo());
        var ok = Assert.IsType<OkObjectResult>(await ctrl.Get());
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;
        Assert.Equal(5, json.GetArrayLength());
    }

    [Fact]
    public async Task Protocols_Zone2_ShowsBehind_WhenLowMinutes()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new FakeRepo();
        repo.ActivityData.Add(new DailyActivity { Id = "pz1", Day = today, MediumActivityMinutes = 10 });

        var ok = Assert.IsType<OkObjectResult>(await new ProtocolsController(repo).Get());
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;

        var zone2 = json[0];
        Assert.Equal("Zone 2 Cardio", zone2.GetProperty("name").GetString());
        Assert.Equal("behind", zone2.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Protocols_Zone2_ShowsOnTrack_WhenSufficientMinutes()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new FakeRepo();
        for (int i = 0; i < 7; i++)
            repo.ActivityData.Add(new DailyActivity { Id = $"pza{i}", Day = today.AddDays(-i), MediumActivityMinutes = 30 });

        var ok = Assert.IsType<OkObjectResult>(await new ProtocolsController(repo).Get());
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;
        Assert.Equal("on-track", json[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Protocols_SleepOptimization_ComputesBedtimeConsistency()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new FakeRepo();
        for (int i = 0; i < 5; i++)
        {
            var day = today.AddDays(-i);
            repo.SleepData.Add(new SleepSession
            {
                Id = $"psl{i}", Day = day,
                BedtimeStart = day.AddDays(-1).ToDateTime(new TimeOnly(23, 0)),
                BedtimeEnd = day.ToDateTime(new TimeOnly(7, 0)),
                TotalSleepMinutes = 480, DeepMinutes = 90, RemMinutes = 100, LightMinutes = 250, AwakeMinutes = 20,
            });
        }

        var ok = Assert.IsType<OkObjectResult>(await new ProtocolsController(repo).Get());
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;
        var sleepProto = json[1];
        Assert.Equal("Sleep Optimization", sleepProto.GetProperty("name").GetString());
        Assert.Equal("on-track", sleepProto.GetProperty("status").GetString());
    }
}

// ── Stress Controller ──

public class StressControllerTests
{
    [Fact]
    public async Task Stress_ReturnsEmptyList_WhenNoData()
    {
        var ok = Assert.IsType<OkObjectResult>(await new StressController(new FakeRepo()).Get(7));
        var list = Assert.IsAssignableFrom<List<DailyStress>>(ok.Value);
        Assert.Empty(list);
    }
}

// ── Domain Entity Tests ──

public class EntityTests
{
    [Fact]
    public void SleepEfficiency_ComputesCorrectly()
    {
        var s = new SleepSession
        {
            BedtimeStart = new DateTime(2026, 6, 10, 23, 0, 0),
            BedtimeEnd = new DateTime(2026, 6, 11, 7, 0, 0),
            TotalSleepMinutes = 420,
        };
        Assert.Equal(0.875, s.Efficiency, 3);
    }

    [Fact]
    public void SleepEfficiency_ReturnsZero_WhenNoSleep()
    {
        var s = new SleepSession
        {
            BedtimeStart = DateTime.UtcNow,
            BedtimeEnd = DateTime.UtcNow,
            TotalSleepMinutes = 0,
        };
        Assert.Equal(0, s.Efficiency);
    }
}
