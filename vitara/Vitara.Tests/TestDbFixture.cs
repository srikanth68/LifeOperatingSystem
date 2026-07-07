using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vitara.Infrastructure.Data;

namespace Vitara.Tests;

public class TestDbFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    public VitaraDbContext Db { get; }

    public TestDbFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var opts = new DbContextOptionsBuilder<VitaraDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new VitaraDbContext(opts);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}

public static class TestHelper
{
    public static (VitaraDbContext ctx, VitaraRepository repo) CreateFreshDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<VitaraDbContext>()
            .UseSqlite(conn)
            .Options;
        var ctx = new VitaraDbContext(opts);
        ctx.Database.EnsureCreated();
        return (ctx, new VitaraRepository(ctx));
    }
}
