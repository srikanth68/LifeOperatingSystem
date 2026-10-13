using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NorthStar.Domain.Entities;
using NorthStar.Infrastructure.Data;

namespace NorthStar.Tests;

// The lookups San makes before reading an email: earlier messages from the same sender
// or thread, so a reply is read beside what came before it.
public class RelatedContextTests
{
    private static async Task<(SqliteConnection Conn, NorthStarRepository Repo)> NewAsync()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var db = new NorthStarDbContext(new DbContextOptionsBuilder<NorthStarDbContext>().UseSqlite(conn).Options);
        await db.Database.EnsureCreatedAsync();
        await MemorySearch.EnsureSchemaAsync(conn);
        return (conn, new NorthStarRepository(db));
    }

    private static ActivityEvent Email(string key, string detail, int daysAgo = 1, string source = "email") => new()
    {
        Source = source, Kind = "email", Title = key, Detail = detail,
        OccurredAt = DateTime.UtcNow.AddDays(-daysAgo), EventKey = $"{source}:{key}",
    };

    [Fact]
    public async Task FindsEarlierMailFromTheSameSenderOrThread()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        await repo.AddEventIfNewAsync(Email("a", "from billing@acme.com · thread t-100 · Invoice March"));
        await repo.AddEventIfNewAsync(Email("b", "from friend@mail.com · thread t-200 · Dinner"));
        await repo.AddEventIfNewAsync(Email("c", "from support@other.com · thread t-100 · Re: Invoice March"));

        var bySender = await repo.FindEventsAsync("email", ["BILLING@acme.com"]);
        var byThread = await repo.FindEventsAsync("email", ["t-100"]);

        Assert.Equal("a", Assert.Single(bySender).Title);
        Assert.Equal(2, byThread.Count);
    }

    [Fact]
    public async Task OtherSourcesOldEventsAndTinyNeedlesAreIgnored()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        await repo.AddEventIfNewAsync(Email("old", "from billing@acme.com", daysAgo: 400));
        await repo.AddEventIfNewAsync(Email("vault", "from billing@acme.com", source: "vault"));

        Assert.Empty(await repo.FindEventsAsync("email", ["billing@acme.com"]));
        // A two-letter needle would match nearly everything.
        Assert.Empty(await repo.FindEventsAsync("email", ["ac", ""]));
    }
}
