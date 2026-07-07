using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vitara.Domain.Entities;
using Vitara.Infrastructure.Data;

namespace Vitara.Tests;

public class MigrationTests
{
    [Fact]
    public async Task CreateMissingTables_CreatesAllTables_OnFreshDb()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var opts = new DbContextOptionsBuilder<VitaraDbContext>().UseSqlite(conn).Options;
        using var ctx = new VitaraDbContext(opts);
        await ctx.Database.EnsureCreatedAsync();

        await VitaraDbContext.CreateMissingTablesAsync(ctx);

        var tables = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }

        Assert.Contains("Profiles", tables);
        Assert.Contains("Stress", tables);
        Assert.Contains("Resilience", tables);
        Assert.Contains("CardiovascularAge", tables);
        Assert.Contains("Spo2", tables);
        Assert.Contains("Vo2Max", tables);
        Assert.Contains("Workouts", tables);
        Assert.Contains("HeartRate", tables);
    }

    [Fact]
    public async Task CreateMissingTables_IsIdempotent()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var opts = new DbContextOptionsBuilder<VitaraDbContext>().UseSqlite(conn).Options;
        using var ctx = new VitaraDbContext(opts);
        await ctx.Database.EnsureCreatedAsync();

        await VitaraDbContext.CreateMissingTablesAsync(ctx);
        await VitaraDbContext.CreateMissingTablesAsync(ctx);
        await VitaraDbContext.CreateMissingTablesAsync(ctx);
    }

    [Fact]
    public async Task CreateMissingTables_PreservesExistingData()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var opts = new DbContextOptionsBuilder<VitaraDbContext>().UseSqlite(conn).Options;
        using var ctx = new VitaraDbContext(opts);
        await ctx.Database.EnsureCreatedAsync();

        ctx.Tokens.Add(new OuraToken
        {
            Id = 1,
            AccessToken = "keep-me",
            RefreshToken = "safe",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            LinkedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        await VitaraDbContext.CreateMissingTablesAsync(ctx);

        var token = await ctx.Tokens.FirstOrDefaultAsync();
        Assert.NotNull(token);
        Assert.Equal("keep-me", token!.AccessToken);
    }

    [Fact]
    public async Task CreateMissingTables_NewTablesAreUsable()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var opts = new DbContextOptionsBuilder<VitaraDbContext>().UseSqlite(conn).Options;
        using var ctx = new VitaraDbContext(opts);
        await ctx.Database.EnsureCreatedAsync();
        await VitaraDbContext.CreateMissingTablesAsync(ctx);

        ctx.Profiles.Add(new UserProfile { Id = "usable", Age = 30 });
        ctx.Stress.Add(new DailyStress { Id = "us1", Day = new DateOnly(2026, 6, 10), DaySummary = "normal" });
        ctx.HeartRate.Add(new HeartRateSample { Timestamp = DateTime.UtcNow, Bpm = 72 });
        ctx.Workouts.Add(new Workout { Id = "uw1", Day = new DateOnly(2026, 6, 10), Activity = "running" });
        await ctx.SaveChangesAsync();

        Assert.Equal(30, (await ctx.Profiles.FirstAsync()).Age);
        Assert.Single(await ctx.Stress.ToListAsync());
        Assert.Single(await ctx.HeartRate.ToListAsync());
        Assert.Single(await ctx.Workouts.ToListAsync());
    }
}
