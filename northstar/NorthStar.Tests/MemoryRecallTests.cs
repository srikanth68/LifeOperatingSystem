using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NorthStar.Domain.Entities;
using NorthStar.Infrastructure.Data;

namespace NorthStar.Tests;

// Recall decides what San is reminded of on every chat turn. These run against a real
// SQLite FTS5 index built by the same schema code production uses.
public class MemoryRecallTests
{
    private static async Task<(SqliteConnection Conn, NorthStarDbContext Db, NorthStarRepository Repo)> NewAsync()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var db = new NorthStarDbContext(new DbContextOptionsBuilder<NorthStarDbContext>().UseSqlite(conn).Options);
        await db.Database.EnsureCreatedAsync();
        await MemorySearch.EnsureSchemaAsync(conn);
        return (conn, db, new NorthStarRepository(db));
    }

    private static Task<MemoryEntry> Save(NorthStarRepository repo, string content,
        int importance = 3, int daysAgo = 0, string kind = "observation") =>
        repo.SaveMemoryAsync(new MemoryEntry
        {
            Content = content, Importance = importance, Kind = kind,
            CreatedAt = DateTime.UtcNow.AddDays(-daysAgo),
        });

    // The bug that started this: "is" and "my" matched every memory that contained them.
    [Fact]
    public async Task FillerWordsNoLongerMatchEverything()
    {
        var (conn, _, repo) = await NewAsync();
        using var _c = conn;
        await Save(repo, "My car is a blue Honda Civic");
        await Save(repo, "The dentist appointment is on Friday");

        var found = await repo.RecallMemoriesAsync("what is my car", null, 8);

        Assert.Single(found);
        Assert.Contains("Honda", found[0].Content);
    }

    [Fact]
    public async Task PluralsFindSingulars()
    {
        var (conn, _, repo) = await NewAsync();
        using var _c = conn;
        await Save(repo, "Weekly meetings with the design team on Tuesdays");

        Assert.Single(await repo.RecallMemoriesAsync("any meeting this week", null, 8));
    }

    [Fact]
    public async Task PrefixFindsAPartialWord()
    {
        var (conn, _, repo) = await NewAsync();
        using var _c = conn;
        await Save(repo, "My car is a blue Honda Civic");

        Assert.Single(await repo.RecallMemoriesAsync("hond", null, 8));
    }

    [Fact]
    public async Task ImportanceOrdersMemoriesThatMatchAboutEqually()
    {
        var (conn, _, repo) = await NewAsync();
        using var _c = conn;
        await Save(repo, "Coffee order is a flat white", importance: 1);
        await Save(repo, "Coffee order is an oat latte", importance: 5);

        var found = await repo.RecallMemoriesAsync("coffee order", null, 8);

        Assert.Equal(2, found.Count);
        Assert.Contains("oat latte", found[0].Content);
    }

    [Fact]
    public async Task NewerWinsWhenOtherwiseEqual()
    {
        var (conn, _, repo) = await NewAsync();
        using var _c = conn;
        await Save(repo, "Gym membership is at the downtown branch", daysAgo: 400);
        await Save(repo, "Gym membership is at the riverside branch", daysAgo: 2);

        var found = await repo.RecallMemoriesAsync("gym membership", null, 8);

        Assert.Contains("riverside", found[0].Content);
    }

    // A strong text match must not be buried under a weak one just because the weak one
    // was marked important.
    [Fact]
    public async Task ImportanceCannotLiftAPoorMatchOverAGoodOne()
    {
        var (conn, _, repo) = await NewAsync();
        using var _c = conn;
        await Save(repo, "Passport renewal due in March, passport photos needed for the passport form", importance: 2);
        await Save(repo, "Booked flights for March; bring the travel adapter and the passport and snacks and chargers", importance: 5);

        var found = await repo.RecallMemoriesAsync("passport renewal", null, 8);

        Assert.Contains("renewal", found[0].Content);
    }

    [Fact]
    public async Task TheSameMemorySavedTwiceComesBackOnce()
    {
        var (conn, _, repo) = await NewAsync();
        using var _c = conn;
        await Save(repo, "Prefers aisle seats on flights.");
        await Save(repo, "prefers aisle seats on flights");

        Assert.Single(await repo.RecallMemoriesAsync("seats on flights", null, 8));
    }

    [Fact]
    public async Task AMessageWithNoTopicWordsRecallsNothing()
    {
        var (conn, _, repo) = await NewAsync();
        using var _c = conn;
        await Save(repo, "My car is a blue Honda Civic");

        Assert.Null(MemorySearch.BuildMatch("what do you know about me?"));
        Assert.Empty(await repo.RecallMemoriesAsync("what do you know about me?", null, 8));
    }

    [Fact]
    public async Task QueryTextCannotBreakTheMatchSyntax()
    {
        var (conn, _, repo) = await NewAsync();
        using var _c = conn;
        await Save(repo, "My car is a blue Honda Civic");

        var found = await repo.RecallMemoriesAsync("car\" OR *) NEAR( \"", null, 8);

        Assert.Single(found);
    }

    [Fact]
    public async Task KindFilterStillApplies()
    {
        var (conn, _, repo) = await NewAsync();
        using var _c = conn;
        await Save(repo, "Decided to sell the old bike", kind: "decision");
        await Save(repo, "The old bike needs a new chain", kind: "observation");

        var found = await repo.RecallMemoriesAsync("old bike", "decision", 8);

        Assert.Single(found);
        Assert.Equal("decision", found[0].Kind);
    }

    [Fact]
    public async Task RecallIsStillRecordedAsReinforcement()
    {
        var (conn, db, repo) = await NewAsync();
        using var _c = conn;
        var saved = await Save(repo, "My car is a blue Honda Civic");

        await repo.RecallMemoriesAsync("honda", null, 8);

        var reloaded = await db.Memories.AsNoTracking().SingleAsync(m => m.Id == saved.Id);
        Assert.Equal(1, reloaded.AccessCount);
        Assert.NotNull(reloaded.LastAccessedAt);
    }

    // Existing databases have an index built without stemming; the migration rebuilds it
    // from the Memories table without losing a row.
    [Fact]
    public async Task AnIndexBuiltBeforeStemmingIsRebuiltInPlace()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var db = new NorthStarDbContext(new DbContextOptionsBuilder<NorthStarDbContext>().UseSqlite(conn).Options);
        await db.Database.EnsureCreatedAsync();

        await using (var old = conn.CreateCommand())
        {
            old.CommandText = """
                CREATE VIRTUAL TABLE MemoryFts USING fts5(Content, Tags, content='Memories', content_rowid='rowid');
                CREATE TRIGGER Memories_ai AFTER INSERT ON Memories BEGIN
                    INSERT INTO MemoryFts(rowid, Content, Tags) VALUES (new.rowid, new.Content, new.Tags);
                END;
                """;
            await old.ExecuteNonQueryAsync();
        }
        var repo = new NorthStarRepository(db);
        await Save(repo, "Weekly meetings with the design team");

        await MemorySearch.EnsureSchemaAsync(conn);

        await using (var probe = conn.CreateCommand())
        {
            probe.CommandText = "SELECT sql FROM sqlite_master WHERE name = 'MemoryFts'";
            Assert.Contains("porter", (string)(await probe.ExecuteScalarAsync())!);
        }
        Assert.Single(await repo.RecallMemoriesAsync("meeting", null, 8));

        // And the rebuilt triggers keep indexing new memories.
        await Save(repo, "Meetings moved to Wednesdays");
        Assert.Equal(2, (await repo.RecallMemoriesAsync("meeting", null, 8)).Count);
    }

    [Fact]
    public void RelevanceIsAShareOfTheBestMatch_NotStretchedToTheExtremes()
    {
        var rel = MemorySearch.NormaliseBm25([-2.0, -1.9]);
        Assert.Equal(1.0, rel[0], 3);
        Assert.Equal(0.95, rel[1], 3);
    }
}
