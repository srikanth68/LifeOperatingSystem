using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NorthStar.Domain.Entities;
using NorthStar.Infrastructure.Data;

namespace NorthStar.Tests;

// Ingest chooses between rewriting a day's row and appending a new one, and the
// choice is destructive in one direction: an upsert applied to something that should
// have been history silently overwrites it. These pin the routing rule down.
public class KnowledgeIngestTests
{
    private static async Task<(SqliteConnection Conn, NorthStarDbContext Db)> NewDbAsync()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<NorthStarDbContext>().UseSqlite(conn).Options;
        var db = new NorthStarDbContext(opts);
        await db.Database.EnsureCreatedAsync();
        return (conn, db);
    }

    private static readonly DateOnly Day = new(2026, 8, 9);

    [Fact]
    public async Task SameTopicSameDay_RewritesOneRow()
    {
        var (conn, db) = await NewDbAsync();
        using var _ = conn;
        var repo = new NorthStarRepository(db);

        var (first, created1) = await repo.UpsertDailyEntryAsync(
            "san-audit", "bill.amc", "AMC bill $45 due Aug 9", null, Day);
        var (second, created2) = await repo.UpsertDailyEntryAsync(
            "san-audit", "bill.amc", "AMC bill $45 is overdue, late fee applied", null, Day);

        Assert.True(created1);
        Assert.False(created2);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(await db.Entries.ToListAsync());
        // The brain holds the LATEST understanding, not the first one it was given.
        Assert.Equal("AMC bill $45 is overdue, late fee applied",
            (await db.Entries.SingleAsync()).Summary);
    }

    [Fact]
    public async Task SameTopicDifferentDay_KeepsBothDays()
    {
        var (conn, db) = await NewDbAsync();
        using var _ = conn;
        var repo = new NorthStarRepository(db);

        await repo.UpsertDailyEntryAsync("san-audit", "bill.amc", "due Aug 9", null, Day);
        await repo.UpsertDailyEntryAsync("san-audit", "bill.amc", "still unpaid Aug 10", null, Day.AddDays(1));

        Assert.Equal(2, await db.Entries.CountAsync());
    }

    [Fact]
    public async Task DifferentSources_DoNotCollide()
    {
        var (conn, db) = await NewDbAsync();
        using var _ = conn;
        var repo = new NorthStarRepository(db);

        // The audit and triage share San's ledger key namespace, but they are distinct
        // NorthStar sources and must not overwrite each other's view of a topic.
        await repo.UpsertDailyEntryAsync("san-audit", "bill.amc", "seen in the Vault snapshot", null, Day);
        await repo.UpsertDailyEntryAsync("san-email", "bill.amc", "seen in an email from AMC", null, Day);

        Assert.Equal(2, await db.Entries.CountAsync());
    }

    [Fact]
    public async Task LaterWriteWithoutRawJson_DoesNotEraseTheStoredPayload()
    {
        var (conn, db) = await NewDbAsync();
        using var _ = conn;
        var repo = new NorthStarRepository(db);

        await repo.UpsertDailyEntryAsync("vault", "spending", "spent $200", """{"total":200}""", Day);
        await repo.UpsertDailyEntryAsync("vault", "spending", "spent $260", null, Day);

        var entry = await db.Entries.SingleAsync();
        Assert.Equal("spent $260", entry.Summary);
        Assert.Equal("""{"total":200}""", entry.RawJson);
    }

    [Fact]
    public async Task AppendPath_KeepsEveryEntry()
    {
        var (conn, db) = await NewDbAsync();
        using var _ = conn;
        var repo = new NorthStarRepository(db);

        // What save_knowledge does: no day, so each fact San learns mid-conversation
        // is its own row and nothing it learned earlier is overwritten.
        for (var i = 0; i < 3; i++)
            await repo.AddEntryAsync(new KnowledgeEntry
            {
                Source = "san", Topic = "general", Summary = $"fact {i}", Day = null,
            });

        Assert.Equal(3, await db.Entries.CountAsync());
    }
}
