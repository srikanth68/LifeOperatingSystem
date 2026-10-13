using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Infrastructure.Data;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// How long a finding lives (decided 2026-10-13): it closes only after two clear days in a
// row, a return within seven days continues the same episode, and the load ratio has to
// sit outside its band for two days before it is a finding at all.
public class FindingTimingTests : IDisposable
{
    private readonly TestDbFixture _fx = new();
    private VitaraRepository Repo => new(_fx.Db);
    private static readonly DateOnly Day1 = new(2026, 9, 1);

    private static Finding Off(DateOnly day) => new()
    {
        Key = "deviation:resting_hr:high",
        Type = FindingTypes.Deviation,
        Metric = MetricKeys.RestingHeartRate,
        Direction = "high",
        Summary = "Resting heart rate is above your normal.",
        FirstDetectedLocal = day,
        LastDetectedLocal = day,
    };

    [Fact]
    public async Task OneNormalMorningDoesNotCloseAFinding_TwoInARowDo()
    {
        await Repo.SyncFindingsAsync([Off(Day1)], Day1);
        await Repo.SyncFindingsAsync([Off(Day1.AddDays(1))], Day1.AddDays(1));

        var first = await Repo.SyncFindingsAsync([], Day1.AddDays(2));     // one clear day
        Assert.Equal(0, first.Resolved);
        Assert.Single(await Repo.GetFindingsAsync(activeOnly: true));

        var second = await Repo.SyncFindingsAsync([], Day1.AddDays(3));    // two in a row
        Assert.Equal(1, second.Resolved);
        Assert.Empty(await Repo.GetFindingsAsync(activeOnly: true));
    }

    [Fact]
    public async Task AReturnWithinAWeekContinuesTheSameEpisode()
    {
        await Repo.SyncFindingsAsync([Off(Day1)], Day1);
        await Repo.SyncFindingsAsync([], Day1.AddDays(1));
        await Repo.SyncFindingsAsync([], Day1.AddDays(2));                 // closed

        var back = await Repo.SyncFindingsAsync([Off(Day1.AddDays(5))], Day1.AddDays(5));

        Assert.Equal(0, back.Opened);
        var f = Assert.Single(await Repo.GetFindingsAsync(activeOnly: true));
        Assert.Equal(Day1, f.FirstDetectedLocal);                          // still day 1 of the same episode
    }

    [Fact]
    public async Task AReturnAfterAWeekIsANewFinding()
    {
        await Repo.SyncFindingsAsync([Off(Day1)], Day1);
        await Repo.SyncFindingsAsync([], Day1.AddDays(1));
        await Repo.SyncFindingsAsync([], Day1.AddDays(2));

        var later = await Repo.SyncFindingsAsync([Off(Day1.AddDays(20))], Day1.AddDays(20));

        Assert.Equal(1, later.Opened);
    }

    private static DerivedMetric Acwr(DateOnly day, double value) =>
        new() { Metric = "acwr_active_calories", ObservedDateLocal = day, Value = value };

    [Fact]
    public void OneDayAboveTheLoadBandIsNotYetAFinding_TwoAre()
    {
        var today = Day1.AddDays(30);

        var oneDay = FindingRun.Detect(new FindingRunInputs([], [], [Acwr(today.AddDays(-1), 1.1), Acwr(today, 1.6)], today));
        Assert.DoesNotContain(oneDay, f => f.Type == FindingTypes.StrainRisk);

        var twoDays = FindingRun.Detect(new FindingRunInputs([], [], [Acwr(today.AddDays(-1), 1.5), Acwr(today, 1.6)], today));
        Assert.Contains(twoDays, f => f.Type == FindingTypes.StrainRisk);
    }

    public void Dispose() => _fx.Dispose();
}
