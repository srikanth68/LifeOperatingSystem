using Vitara.Domain.Entities;
using Vitara.Infrastructure.Data;

namespace Vitara.Tests;

public class RepositoryTests
{
    private static VitaraRepository Repo() => TestHelper.CreateFreshDb().repo;

    // ── Profile ──

    [Fact]
    public async Task GetProfile_ReturnsNull_WhenEmpty()
    {
        Assert.Null(await Repo().GetProfileAsync());
    }

    [Fact]
    public async Task SaveProfile_ThenGet_RoundTrips()
    {
        var repo = Repo();
        await repo.SaveProfileAsync(new UserProfile { Id = "test", Age = 38, Weight = 88, Height = 1.7, BiologicalSex = "male" });
        var p = await repo.GetProfileAsync();
        Assert.NotNull(p);
        Assert.Equal(38, p!.Age);
        Assert.Equal("male", p.BiologicalSex);
    }

    [Fact]
    public async Task SaveProfile_Updates_ExistingRecord()
    {
        var repo = Repo();
        await repo.SaveProfileAsync(new UserProfile { Id = "upd", Age = 30 });
        await repo.SaveProfileAsync(new UserProfile { Id = "upd", Age = 31 });
        var p = await repo.GetProfileAsync();
        Assert.Equal(31, p!.Age);
    }

    // ── Sleep ──

    [Fact]
    public async Task UpsertSleep_ThenGet_FiltersByDateRange()
    {
        var repo = Repo();
        await repo.UpsertSleepAsync(new[]
        {
            MakeSleep("s1", new DateOnly(2026, 6, 10)),
            MakeSleep("s2", new DateOnly(2026, 6, 15)),
            MakeSleep("s3", new DateOnly(2026, 6, 20)),
        });

        var result = await repo.GetSleepAsync(new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 18));
        Assert.Single(result);
        Assert.Equal("s2", result[0].Id);
    }

    [Fact]
    public async Task UpsertSleep_UpdatesExisting_OnSameId()
    {
        var repo = Repo();
        await repo.UpsertSleepAsync(new[] { MakeSleep("dup1", new DateOnly(2026, 6, 1), deepMin: 60) });
        await repo.UpsertSleepAsync(new[] { MakeSleep("dup1", new DateOnly(2026, 6, 1), deepMin: 90) });

        var result = await repo.GetSleepAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1));
        Assert.Single(result);
        Assert.Equal(90, result[0].DeepMinutes);
    }

    [Fact]
    public async Task GetSleep_ReturnsOrderedByDay()
    {
        var repo = Repo();
        await repo.UpsertSleepAsync(new[]
        {
            MakeSleep("ord3", new DateOnly(2026, 5, 3)),
            MakeSleep("ord1", new DateOnly(2026, 5, 1)),
            MakeSleep("ord2", new DateOnly(2026, 5, 2)),
        });

        var result = await repo.GetSleepAsync(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3));
        Assert.Equal(new DateOnly(2026, 5, 1), result[0].Day);
        Assert.Equal(new DateOnly(2026, 5, 3), result[^1].Day);
    }

    [Fact]
    public async Task GetSleep_ReturnsEmpty_WhenNoMatch()
    {
        var repo = Repo();
        await repo.UpsertSleepAsync(new[] { MakeSleep("nomatch", new DateOnly(2026, 1, 1)) });
        var result = await repo.GetSleepAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        Assert.Empty(result);
    }

    // ── Readiness ──

    [Fact]
    public async Task UpsertReadiness_ThenGet_Works()
    {
        var repo = Repo();
        await repo.UpsertReadinessAsync(new[]
        {
            new DailyReadiness { Id = "r1", Day = new DateOnly(2026, 6, 10), Score = 85, Level = "optimal" },
            new DailyReadiness { Id = "r2", Day = new DateOnly(2026, 6, 11), Score = 72, Level = "good" },
        });

        var result = await repo.GetReadinessAsync(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 11));
        Assert.Equal(2, result.Count);
        Assert.Equal("optimal", result[0].Level);
    }

    // ── Activity ──

    [Fact]
    public async Task UpsertActivity_ThenGet_Works()
    {
        var repo = Repo();
        await repo.UpsertActivityAsync(new[]
        {
            new DailyActivity { Id = "a1", Day = new DateOnly(2026, 6, 10), Steps = 8000, Score = 70 },
        });

        var result = await repo.GetActivityAsync(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10));
        Assert.Single(result);
        Assert.Equal(8000, result[0].Steps);
    }

    // ── Stress ──

    [Fact]
    public async Task UpsertStress_ThenGet_Works()
    {
        var repo = Repo();
        await repo.UpsertStressAsync(new[]
        {
            new DailyStress { Id = "st1", Day = new DateOnly(2026, 6, 10), DaySummary = "normal", StressHighSeconds = 120 },
        });

        var result = await repo.GetStressAsync(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10));
        Assert.Single(result);
        Assert.Equal("normal", result[0].DaySummary);
        Assert.Equal(120, result[0].StressHighSeconds);
    }

    // ── Resilience ──

    [Fact]
    public async Task UpsertResilience_ThenGet_Works()
    {
        var repo = Repo();
        await repo.UpsertResilienceAsync(new[]
        {
            new DailyResilience { Id = "res1", Day = new DateOnly(2026, 6, 10), Level = "strong", SleepRecovery = 80 },
        });

        var result = await repo.GetResilienceAsync(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10));
        Assert.Single(result);
        Assert.Equal("strong", result[0].Level);
    }

    // ── CardiovascularAge ──

    [Fact]
    public async Task UpsertCardiovascularAge_ThenGet_Works()
    {
        var repo = Repo();
        await repo.UpsertCardiovascularAgeAsync(new[]
        {
            new DailyCardiovascularAge { Id = "cv1", Day = new DateOnly(2026, 6, 10), VascularAge = 35.5 },
        });

        var result = await repo.GetCardiovascularAgeAsync(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10));
        Assert.Single(result);
        Assert.Equal(35.5, result[0].VascularAge);
    }

    // ── SpO2 ──

    [Fact]
    public async Task UpsertSpo2_ThenGet_Works()
    {
        var repo = Repo();
        await repo.UpsertSpo2Async(new[]
        {
            new DailySpo2 { Id = "sp1", Day = new DateOnly(2026, 6, 10), Spo2Average = 97.5 },
        });

        var result = await repo.GetSpo2Async(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10));
        Assert.Single(result);
        Assert.Equal(97.5, result[0].Spo2Average);
    }

    // ── HeartRate ──

    [Fact]
    public async Task UpsertHeartRate_ThenGet_FiltersByTimestamp()
    {
        var repo = Repo();
        var t1 = new DateTime(2026, 6, 10, 8, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 6, 10, 20, 0, 0, DateTimeKind.Utc);

        await repo.UpsertHeartRateAsync(new[]
        {
            new HeartRateSample { Timestamp = t1, Bpm = 60 },
            new HeartRateSample { Timestamp = t2, Bpm = 75 },
            new HeartRateSample { Timestamp = t3, Bpm = 68 },
        });

        var result = await repo.GetHeartRateAsync(
            new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 10, 22, 0, 0, DateTimeKind.Utc));
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task UpsertHeartRate_SkipsDuplicates()
    {
        var repo = Repo();
        var ts = new DateTime(2026, 6, 11, 8, 0, 0, DateTimeKind.Utc);
        await repo.UpsertHeartRateAsync(new[] { new HeartRateSample { Timestamp = ts, Bpm = 60 } });
        await repo.UpsertHeartRateAsync(new[] { new HeartRateSample { Timestamp = ts, Bpm = 60 } });

        var result = await repo.GetHeartRateAsync(
            new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc));
        Assert.Single(result);
    }

    // ── VO2 Max ──

    [Fact]
    public async Task UpsertVo2Max_ThenGet_Works()
    {
        var repo = Repo();
        await repo.UpsertVo2MaxAsync(new[]
        {
            new Vo2MaxRecord { Id = "vo1", Day = new DateOnly(2026, 6, 10), Vo2Max = 42.5 },
        });

        var result = await repo.GetVo2MaxAsync(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10));
        Assert.Single(result);
        Assert.Equal(42.5, result[0].Vo2Max);
    }

    // ── Workouts ──

    [Fact]
    public async Task UpsertWorkouts_ThenGet_Works()
    {
        var repo = Repo();
        await repo.UpsertWorkoutsAsync(new[]
        {
            new Workout { Id = "w1", Day = new DateOnly(2026, 6, 10), Activity = "running", Intensity = "hard", Calories = 300 },
            new Workout { Id = "w2", Day = new DateOnly(2026, 6, 10), Activity = "walking", Intensity = "moderate", Calories = 100 },
        });

        var result = await repo.GetWorkoutsAsync(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10));
        Assert.Equal(2, result.Count);
    }

    // ── Sync Tracking ──

    [Fact]
    public async Task GetLatestDay_ReturnsNull_WhenEmpty()
    {
        Assert.Null(await Repo().GetLatestDayAsync());
    }

    [Fact]
    public async Task GetLatestDay_ReturnsMinOfMaxDays()
    {
        var repo = Repo();
        await repo.UpsertSleepAsync(new[] { MakeSleep("sync-s", new DateOnly(2026, 6, 15)) });
        await repo.UpsertReadinessAsync(new[] { new DailyReadiness { Id = "sync-r", Day = new DateOnly(2026, 6, 12) } });
        await repo.UpsertActivityAsync(new[] { new DailyActivity { Id = "sync-a", Day = new DateOnly(2026, 6, 18) } });

        var latest = await repo.GetLatestDayAsync();
        Assert.Equal(new DateOnly(2026, 6, 12), latest);
    }

    // ── Token ──

    [Fact]
    public async Task Token_SaveGet_Delete_Lifecycle()
    {
        var repo = Repo();
        Assert.Null(await repo.GetTokenAsync());

        await repo.SaveTokenAsync(new OuraToken
        {
            Id = 1,
            AccessToken = "abc",
            RefreshToken = "def",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            LinkedAt = DateTime.UtcNow
        });
        var t = await repo.GetTokenAsync();
        Assert.NotNull(t);
        Assert.Equal("abc", t!.AccessToken);

        await repo.DeleteTokenAsync();
        Assert.Null(await repo.GetTokenAsync());
    }

    // ── Helpers ──

    private static SleepSession MakeSleep(string id, DateOnly day, int deepMin = 60) => new()
    {
        Id = id,
        Day = day,
        BedtimeStart = day.ToDateTime(new TimeOnly(23, 0)),
        BedtimeEnd = day.AddDays(1).ToDateTime(new TimeOnly(7, 0)),
        TotalSleepMinutes = 420,
        DeepMinutes = deepMin,
        RemMinutes = 90,
        LightMinutes = 180,
        AwakeMinutes = 30,
        Score = 80,
        AvgHrv = 35.0,
        LowestHr = 55.0,
    };
}
