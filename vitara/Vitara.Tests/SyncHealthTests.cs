using Vitara.Domain.Entities;
using Vitara.Infrastructure.Data;

namespace Vitara.Tests;

// The failures these cover are all the same shape: the module could not tell you when
// it did not know something. A stale average read exactly like a fresh one, a sync that
// failed stamped itself as a success, and a collection that stopped returning data was
// never asked for those days again.
public class SyncHealthTests
{
    private static VitaraRepository Repo() => TestHelper.CreateFreshDb().repo;

    // ── Per-collection watermarks ──

    [Fact]
    public async Task EachCollectionReportsItsOwnNewestDay()
    {
        var repo = Repo();
        await repo.UpsertSleepAsync([new SleepSession { Id = "s1", Day = new DateOnly(2026, 9, 1) }]);
        await repo.UpsertSpo2Async([new DailySpo2 { Id = "p1", Day = new DateOnly(2026, 8, 1) }]);

        var days = await repo.GetLatestDaysAsync();

        Assert.Equal(new DateOnly(2026, 9, 1), days["sleep"]);
        Assert.Equal(new DateOnly(2026, 8, 1), days["spo2"]);
    }

    [Fact]
    public async Task ACollectionThatHasFallenBehindIsNotDraggedForwardByTheOthers()
    {
        // The bug this replaces: one watermark across sleep/readiness/activity meant a
        // collection stuck a month back was only ever asked for yesterday onwards, so
        // its missing days were never fetched again.
        var repo = Repo();
        await repo.UpsertSleepAsync([new SleepSession { Id = "s1", Day = new DateOnly(2026, 9, 1) }]);
        await repo.UpsertReadinessAsync([new DailyReadiness { Id = "r1", Day = new DateOnly(2026, 9, 1) }]);
        await repo.UpsertActivityAsync([new DailyActivity { Id = "a1", Day = new DateOnly(2026, 9, 1) }]);
        await repo.UpsertStressAsync([new DailyStress { Id = "st1", Day = new DateOnly(2026, 8, 5) }]);

        var days = await repo.GetLatestDaysAsync();

        Assert.Equal(new DateOnly(2026, 8, 5), days["stress"]);
        Assert.True(days["stress"] < days["sleep"], "stress must keep its own, older watermark");
    }

    [Fact]
    public async Task ACollectionWithNoDataIsSimplyAbsent()
    {
        // The worker reads absence as "cold start, reach back 30 days" — which is the
        // right answer for a collection this Oura account has never returned.
        var days = await Repo().GetLatestDaysAsync();
        Assert.False(days.ContainsKey("spo2"));
    }

    // ── Heart rate: batching and pruning ──

    [Fact]
    public async Task RepeatedHeartRateSamplesAreNotStoredTwice()
    {
        // Oura returns overlapping windows between runs, so the same reading arrives
        // again on the next sync.
        var repo = Repo();
        var t = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

        await repo.UpsertHeartRateAsync([new HeartRateSample { Timestamp = t, Bpm = 60 }]);
        await repo.UpsertHeartRateAsync([new HeartRateSample { Timestamp = t, Bpm = 60 }]);

        Assert.Single(await repo.GetHeartRateAsync(t.AddHours(-1), t.AddHours(1)));
    }

    [Fact]
    public async Task DuplicatesWithinOneBatchAreCollapsed()
    {
        // The old per-sample existence check could not see duplicates inside the batch
        // it was currently adding, because none of them were saved yet.
        var repo = Repo();
        var t = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

        await repo.UpsertHeartRateAsync([
            new HeartRateSample { Timestamp = t, Bpm = 60 },
            new HeartRateSample { Timestamp = t, Bpm = 60 },
        ]);

        Assert.Single(await repo.GetHeartRateAsync(t.AddHours(-1), t.AddHours(1)));
    }

    [Fact]
    public async Task SameInstantWithADifferentReadingIsKept()
    {
        var repo = Repo();
        var t = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

        await repo.UpsertHeartRateAsync([
            new HeartRateSample { Timestamp = t, Bpm = 60 },
            new HeartRateSample { Timestamp = t, Bpm = 61 },
        ]);

        Assert.Equal(2, (await repo.GetHeartRateAsync(t.AddHours(-1), t.AddHours(1))).Count);
    }

    [Fact]
    public async Task PruningRemovesOnlyWhatIsOlderThanTheCutoff()
    {
        // Nothing in this module had ever deleted a row, and heart rate is the one table
        // Oura fills continuously.
        var repo = Repo();
        var now = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        await repo.UpsertHeartRateAsync([
            new HeartRateSample { Timestamp = now.AddDays(-200), Bpm = 55 },
            new HeartRateSample { Timestamp = now.AddDays(-100), Bpm = 56 },
            new HeartRateSample { Timestamp = now.AddDays(-10), Bpm = 57 },
        ]);

        var removed = await repo.PruneHeartRateAsync(now.AddDays(-90));

        Assert.Equal(2, removed);
        var left = await repo.GetHeartRateAsync(now.AddYears(-1), now);
        Assert.Single(left);
        Assert.Equal(57, left[0].Bpm);
    }

    [Fact]
    public async Task PruningAnEmptyTableIsHarmless()
        => Assert.Equal(0, await Repo().PruneHeartRateAsync(DateTime.UtcNow));

    // ── Staleness ──

    [Theory]
    // Never synced at all reads as stale: an unlinked-but-present token should not look
    // healthy just because nothing has gone wrong yet.
    [InlineData(null, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]     // one missed daily run is a hiccup, not a fault
    [InlineData(3, true)]
    [InlineData(30, true)]
    public void StalenessIsMeasuredFromTheLastSUCCESSFULSync(int? daysAgo, bool expectedStale)
    {
        var token = new OuraToken
        {
            LastSyncedAt = daysAgo is null ? null : DateTime.UtcNow.AddDays(-daysAgo.Value),
        };

        Assert.Equal(expectedStale, token.IsStale);
    }

    [Fact]
    public void AnAttemptDoesNotCountAsASync()
    {
        // The whole point of splitting the two fields. Previously LastSyncedAt was
        // stamped on every attempt, so a sync that ran and failed every day for a week
        // still reported as having synced moments ago.
        var token = new OuraToken
        {
            LastSyncedAt = DateTime.UtcNow.AddDays(-9),
            LastSyncAttemptAt = DateTime.UtcNow,
            LastSyncError = "spo2; stress",
        };

        Assert.True(token.IsStale);
        Assert.NotNull(token.LastSyncError);
    }
}
