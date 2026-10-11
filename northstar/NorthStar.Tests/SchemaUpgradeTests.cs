using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NorthStar.Domain.Entities;
using NorthStar.Infrastructure.Data;

namespace NorthStar.Tests;

// The live northstar.db predates the lifecycle, provenance, fact history and entities.
// EnsureCreated does nothing to an existing file, so this is what makes it usable.
public class SchemaUpgradeTests
{
    // The Memories and Facts tables exactly as Program.cs created them before.
    private const string OldSchema = """
        CREATE TABLE Facts (
            Key TEXT PRIMARY KEY, Value TEXT NOT NULL DEFAULT '',
            Source TEXT NOT NULL DEFAULT 'manual', UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01'
        );
        CREATE TABLE Memories (
            Id TEXT PRIMARY KEY, Content TEXT NOT NULL DEFAULT '', Kind TEXT NOT NULL DEFAULT 'observation',
            Source TEXT NOT NULL DEFAULT 'agent', Tags TEXT NOT NULL DEFAULT '', Importance INTEGER NOT NULL DEFAULT 3,
            CreatedAt TEXT NOT NULL DEFAULT '0001-01-01', LastAccessedAt TEXT, AccessCount INTEGER NOT NULL DEFAULT 0
        );
        INSERT INTO Memories (Id, Content, Kind, CreatedAt)
            VALUES ('6F9619FF-8B86-D011-B42D-00C04FC964FF', 'Prefers aisle seats on long flights', 'preference', '2026-08-01 10:00:00');
        INSERT INTO Facts (Key, Value, UpdatedAt) VALUES ('name', 'Kanth', '2026-07-01 00:00:00');
        """;

    private static async Task<(SqliteConnection Conn, NorthStarRepository Repo)> OldDatabaseUpgradedAsync()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var cmd = conn.CreateCommand()) { cmd.CommandText = OldSchema; cmd.ExecuteNonQuery(); }

        await NorthStarSchema.UpgradeAsync(conn);
        await MemorySearch.EnsureSchemaAsync(conn);

        var db = new NorthStarDbContext(new DbContextOptionsBuilder<NorthStarDbContext>().UseSqlite(conn).Options);
        return (conn, new NorthStarRepository(db));
    }

    [Fact]
    public async Task ExistingMemoriesComeThroughActiveAndStillRecalled()
    {
        var (conn, repo) = await OldDatabaseUpgradedAsync();
        using var _ = conn;

        var found = Assert.Single(await repo.RecallMemoriesAsync("aisle seats", null, 8));
        Assert.Equal(MemoryStatus.Active, found.Status);
    }

    [Fact]
    public async Task ExistingMemoriesGetAContentKeySoARestatementIsADuplicate()
    {
        var (conn, repo) = await OldDatabaseUpgradedAsync();
        using var _ = conn;

        var r = await repo.RememberAsync(new MemoryEntry { Content = "prefers AISLE seats on long flights!", Kind = "preference" });

        Assert.True(r.Duplicate);
    }

    [Fact]
    public async Task NewTablesWorkOnTheUpgradedFile()
    {
        var (conn, repo) = await OldDatabaseUpgradedAsync();
        using var _ = conn;

        await repo.UpsertFactAsync(new UserFact { Key = "name", Value = "Srikanth" });
        var (e, created, _) = await repo.UpsertEntityAsync("Everest", "place", "mac mini");

        Assert.Equal("Kanth", Assert.Single(await repo.GetFactHistoryAsync("name")).Value);
        Assert.True(created);
        Assert.Equal("mac mini", e!.Aliases);
    }

    [Fact]
    public async Task RunningItTwiceChangesNothing()
    {
        var (conn, _) = await OldDatabaseUpgradedAsync();
        using var _c = conn;

        await NorthStarSchema.UpgradeAsync(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM pragma_table_info('Memories') WHERE name = 'Status'";
        Assert.Equal(1L, cmd.ExecuteScalar());
    }
}
