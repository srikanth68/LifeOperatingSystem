using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vitara.Domain.Entities;
using Vitara.Infrastructure.Data;

namespace Vitara.Tests;

// A manually logged workout was saved correctly and then never seen. Ordering by
// StartTime alone put it behind every Oura workout — StartTime is nullable, only Oura
// fills it, and SQLite sorts NULL last under DESC — and the callers that take the most
// recent 3 (dashboard) or 10 (UI) then cut it off. Stored, and invisible.
public class WorkoutOrderingTests
{
    private static async Task<(SqliteConnection Conn, VitaraRepository Repo)> NewRepoAsync()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<VitaraDbContext>().UseSqlite(conn).Options;
        var ctx = new VitaraDbContext(opts);
        await ctx.Database.EnsureCreatedAsync();
        await VitaraDbContext.CreateMissingTablesAsync(ctx);
        return (conn, new VitaraRepository(ctx));
    }

    private static readonly DateOnly Today = new(2026, 8, 11);

    private static Workout Synced(DateOnly day, string activity, DateTime start) => new()
    {
        Id = Guid.NewGuid().ToString(), Day = day, Activity = activity,
        StartTime = start, Source = "oura",
    };

    // What workout_log and the UI form produce: no StartTime at all.
    private static Workout Manual(DateOnly day, string activity) => new()
    {
        Id = Guid.NewGuid().ToString(), Day = day, Activity = activity,
        StartTime = null, Source = "manual",
    };

    [Fact]
    public async Task TodaysManualWorkoutOutranksOlderSyncedOnes()
    {
        var (conn, repo) = await NewRepoAsync();
        using var _ = conn;

        await repo.UpsertWorkoutsAsync(
        [
            Synced(Today.AddDays(-1), "cycling", new DateTime(2026, 8, 10, 7, 0, 0)),
            Synced(Today.AddDays(-2), "running", new DateTime(2026, 8, 9, 7, 0, 0)),
            Manual(Today, "weights"),
        ]);

        var all = await repo.GetWorkoutsAsync(Today.AddDays(-30), Today);
        Assert.Equal("weights", all[0].Activity);
    }

    // The exact shape of the bug: enough synced history to fill the truncation window.
    [Fact]
    public async Task ManualWorkoutSurvivesTheDashboardsTopThree()
    {
        var (conn, repo) = await NewRepoAsync();
        using var _ = conn;

        var many = Enumerable.Range(1, 10)
            .Select(i => Synced(Today.AddDays(-i), "running", new DateTime(2026, 8, 11, 6, 0, 0).AddDays(-i)))
            .Append(Manual(Today, "weights"));
        await repo.UpsertWorkoutsAsync(many);

        var all = await repo.GetWorkoutsAsync(Today.AddDays(-30), Today);
        Assert.Contains(all.Take(3), w => w.Activity == "weights");   // dashboard
        Assert.Contains(all.Take(10), w => w.Activity == "weights");  // UI list
    }

    [Fact]
    public async Task WithinADayTheTimedWorkoutStillSortsByTime()
    {
        var (conn, repo) = await NewRepoAsync();
        using var _ = conn;

        await repo.UpsertWorkoutsAsync(
        [
            Synced(Today, "morning run", new DateTime(2026, 8, 11, 6, 0, 0)),
            Synced(Today, "evening swim", new DateTime(2026, 8, 11, 19, 0, 0)),
        ]);

        var all = await repo.GetWorkoutsAsync(Today.AddDays(-30), Today);
        Assert.Equal("evening swim", all[0].Activity);
    }

    [Fact]
    public async Task DaysOutsideTheWindowAreExcluded()
    {
        var (conn, repo) = await NewRepoAsync();
        using var _ = conn;

        await repo.UpsertWorkoutsAsync([Manual(Today.AddDays(-60), "ancient"), Manual(Today, "weights")]);

        var all = await repo.GetWorkoutsAsync(Today.AddDays(-30), Today);
        Assert.Single(all);
        Assert.Equal("weights", all[0].Activity);
    }
}
