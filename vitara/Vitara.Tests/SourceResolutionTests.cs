using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Infrastructure.Data;

namespace Vitara.Tests;

// The phone's health app wins for steps, active calories and workouts (Vitara ADR 0025).
public class SourceResolutionTests : IDisposable
{
    private readonly TestDbFixture _fx = new();
    private static readonly DateOnly Day = new(2026, 9, 1);

    private static DailyActivity Ring(int steps = 6200, int cal = 380) => new()
    {
        Id = "oura-1", Day = Day, Score = 81, Steps = steps, ActiveCalories = cal, HighActivityMinutes = 22,
    };

    private static DailyActivity Phone(int steps = 9100, int cal = 450) => new()
    {
        Id = $"applehealth-{Day:yyyy-MM-dd}", Day = Day, Source = SourceResolution.PhoneHealth, Steps = steps, ActiveCalories = cal,
    };

    [Fact]
    public void ThePhonesStepsWin_AndTheRingKeepsWhatOnlyItMeasures()
    {
        var day = Assert.Single(SourceResolution.Activity([Ring(), Phone()]));

        Assert.Equal(9100, day.Steps);
        Assert.Equal(450, day.ActiveCalories);
        Assert.Equal(81, day.Score);              // the ring's own score survives
        Assert.Equal(22, day.HighActivityMinutes);
        Assert.Equal(SourceResolution.PhoneHealth, day.Source);
    }

    [Fact]
    public void APhoneDayWithNoDataNeverWins()
    {
        var day = Assert.Single(SourceResolution.Activity([Ring(), Phone(steps: 0, cal: 0)]));
        Assert.Equal(6200, day.Steps);
    }

    [Fact]
    public void OnlyTheRing_OrOnlyThePhone_IsUsedAsIs()
    {
        Assert.Equal(6200, Assert.Single(SourceResolution.Activity([Ring()])).Steps);
        Assert.Equal(9100, Assert.Single(SourceResolution.Activity([Phone()])).Steps);
    }

    [Fact]
    public async Task BothRowsAreStored_AndReadsAreResolved()
    {
        var repo = new VitaraRepository(_fx.Db);
        await repo.UpsertActivityAsync([Ring(), Phone()]);

        Assert.Equal(2, (await repo.GetActivityRowsAsync(Day, Day)).Count);   // nothing overwritten
        Assert.Equal(9100, Assert.Single(await repo.GetActivityAsync(Day, Day)).Steps);
    }

    private static Workout W(string id, int startHour, int startMinute, int minutes, string? source) => new()
    {
        Id = id, Day = Day, Activity = "running", Source = source,
        StartTime = Day.ToDateTime(new TimeOnly(startHour, startMinute)),
        EndTime = Day.ToDateTime(new TimeOnly(startHour, startMinute)).AddMinutes(minutes),
    };

    [Fact]
    public void TheSameRunSeenByRingAndPhoneIsKeptOnce_AsThePhonesCopy()
    {
        var kept = SourceResolution.Workouts([
            W("oura-w", 7, 2, 40, "autodetected"),
            W("applehealth-20260901070000", 7, 0, 42, SourceResolution.PhoneHealth),
        ]);

        var w = Assert.Single(kept);
        Assert.StartsWith("applehealth-", w.Id);
    }

    [Fact]
    public void SeparateSessionsAndHandLoggedOnesAreAllKept()
    {
        var kept = SourceResolution.Workouts([
            W("oura-w", 18, 0, 30, "autodetected"),
            W("applehealth-20260901070000", 7, 0, 42, SourceResolution.PhoneHealth),
            new Workout { Id = "manual-1", Day = Day, Activity = "yoga", Source = "manual" },
        ]);

        Assert.Equal(3, kept.Count);
    }

    public void Dispose() => _fx.Dispose();
}
