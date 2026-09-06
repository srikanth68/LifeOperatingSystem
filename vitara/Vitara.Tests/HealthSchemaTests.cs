using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Infrastructure.Data;

namespace Vitara.Tests;

// The health tables exist in two places: EF's model, and the hand-written SQL in
// CreateMissingTablesAsync. EnsureCreated builds them from the model on a fresh
// database and does nothing at all on one that already exists -- which is every
// deployed box -- so production only ever sees the hand-written half.
//
// If those two definitions disagree, a fresh developer database works perfectly and
// production throws "no such column" on the first query. These tests exercise the
// production path specifically: raw SQL first, EF reading afterwards.
public class HealthSchemaTests
{
    // A database EF did NOT create, patched only by the hand-written SQL. This is what
    // Everest looks like the first time it starts with these tables.
    private static async Task<VitaraDbContext> ExistingDbPatchedByHandAsync()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        // Stand up the pre-existing schema the way a deployed box already has it, then
        // let the patcher add what is missing -- exactly the production sequence.
        var opts = new DbContextOptionsBuilder<VitaraDbContext>().UseSqlite(conn).Options;
        var db = new VitaraDbContext(opts);
        await db.Database.EnsureCreatedAsync();

        // EnsureCreated has already built everything from the model, so drop the health
        // tables and let the raw SQL rebuild them. Whatever survives this is what
        // production actually runs against.
        foreach (var table in new[] { "Observations", "Devices", "Interventions", "ExcludedPeriods",
                                      "TravelPeriods", "LabPanels", "ReferenceRanges", "Baselines",
                                      "DerivedMetrics", "Findings" })
            await db.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS {table}");

        await VitaraDbContext.CreateMissingTablesAsync(db);
        return db;
    }

    [Fact]
    public async Task ObservationsRoundTripThroughTheHandWrittenSchema()
    {
        await using var db = await ExistingDbPatchedByHandAsync();

        db.Observations.Add(new Observation
        {
            Metric = MetricKeys.SystolicBp,
            Value = 118,
            Unit = "mmHg",
            ObservedAtLocal = new DateTime(2026, 9, 1, 7, 30, 0),
            ObservedDateLocal = new DateOnly(2026, 9, 1),
            Tier = Tiers.Medium,
            Source = "manual",
            BaselineSignature = "position=seated|timeOfDay=waking",
            ContextJson = """{"position":"seated"}""",
            ValueOriginal = 118,
            UnitOriginal = "mmHg",
        });
        await db.SaveChangesAsync();

        var back = await db.Observations.SingleAsync();
        Assert.Equal(MetricKeys.SystolicBp, back.Metric);
        Assert.Equal(new DateOnly(2026, 9, 1), back.ObservedDateLocal);
        Assert.True(back.EligibleForBaseline);
    }

    [Fact]
    public async Task ReIngestingTheSameReadingIsRejectedRatherThanDuplicated()
    {
        // The unique index is what makes a backfill safely resumable: re-running it
        // must not double every row it already wrote.
        await using var db = await ExistingDbPatchedByHandAsync();

        Observation Reading() => new()
        {
            Metric = MetricKeys.RestingHeartRate,
            Value = 54,
            Unit = "bpm",
            ObservedAtLocal = new DateTime(2026, 9, 1, 3, 0, 0),
            ObservedDateLocal = new DateOnly(2026, 9, 1),
            Source = "oura",
        };

        db.Observations.Add(Reading());
        await db.SaveChangesAsync();

        db.Observations.Add(Reading());
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task TheSameMetricFromTwoSourcesOnOneDayIsAllowed()
    {
        // A ring pulse and a cuff pulse on the same morning are both real, and the
        // index must not treat the second as a duplicate of the first.
        await using var db = await ExistingDbPatchedByHandAsync();
        var at = new DateTime(2026, 9, 1, 7, 0, 0);

        db.Observations.Add(new Observation { Metric = MetricKeys.Pulse, Value = 58, ObservedAtLocal = at, ObservedDateLocal = new DateOnly(2026, 9, 1), Source = "oura" });
        db.Observations.Add(new Observation { Metric = MetricKeys.Pulse, Value = 61, ObservedAtLocal = at, ObservedDateLocal = new DateOnly(2026, 9, 1), Source = "manual" });

        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Observations.CountAsync());
    }

    [Fact]
    public async Task EveryContextTableRoundTrips()
    {
        // These are the tables that cannot be reconstructed later, so a schema mismatch
        // in any of them is silent data loss rather than an error someone notices.
        await using var db = await ExistingDbPatchedByHandAsync();

        db.Devices.Add(new Device { Kind = "ring", Model = "Oura Gen3", ActiveFromLocal = new DateOnly(2024, 1, 1) });
        db.Interventions.Add(new Intervention { Kind = "medication", Name = "example", StartedOnLocal = new DateOnly(2026, 6, 1) });
        db.ExcludedPeriods.Add(new ExcludedPeriod { StartLocal = new DateOnly(2026, 3, 1), EndLocal = new DateOnly(2026, 3, 14), Reason = "illness" });
        db.TravelPeriods.Add(new TravelPeriod { StartLocal = new DateOnly(2026, 5, 1), EndLocal = new DateOnly(2026, 5, 10), AwayTz = "Asia/Kolkata" });
        db.LabPanels.Add(new LabPanel { DrawnOnLocal = new DateOnly(2026, 2, 2), LabName = "Quest" });
        db.ReferenceRanges.Add(new ReferenceRange { Metric = MetricKeys.Hba1c, Low = 4.0, High = 5.6, Unit = "%" });
        await db.SaveChangesAsync();

        Assert.Single(await db.Devices.ToListAsync());
        Assert.Single(await db.Interventions.ToListAsync());
        Assert.Single(await db.ExcludedPeriods.ToListAsync());
        Assert.Single(await db.TravelPeriods.ToListAsync());
        Assert.Single(await db.LabPanels.ToListAsync());
        Assert.Single(await db.ReferenceRanges.ToListAsync());
    }

    [Fact]
    public async Task BaselinesDerivedMetricsAndFindingsRoundTrip()
    {
        await using var db = await ExistingDbPatchedByHandAsync();

        db.Baselines.Add(new Baseline
        {
            Metric = MetricKeys.HrvRmssd, ComputedOnLocal = new DateOnly(2026, 9, 1),
            WindowDays = 60, Mean = 48, StdDev = 7, Median = 47, P25 = 43, P75 = 53, N = 55, IsValid = true,
        });
        db.DerivedMetrics.Add(new DerivedMetric { Metric = "hrv_rmssd_z", ObservedDateLocal = new DateOnly(2026, 9, 1), Value = -1.8 });
        db.Findings.Add(new Finding
        {
            Key = "deviation:hrv_rmssd:low", Type = FindingTypes.Deviation, Metric = MetricKeys.HrvRmssd,
            Direction = "low", Summary = "HRV below personal baseline for 2 days.",
            FirstDetectedLocal = new DateOnly(2026, 8, 31), LastDetectedLocal = new DateOnly(2026, 9, 1),
        });
        await db.SaveChangesAsync();

        var finding = await db.Findings.SingleAsync();
        Assert.True(finding.IsActive);
        Assert.True((await db.Baselines.SingleAsync()).IsValid);
    }

    [Fact]
    public async Task RunningTheSchemaPatcherTwiceIsHarmless()
    {
        // It runs on every start of both the API and the worker, with no ordering
        // between them.
        await using var db = await ExistingDbPatchedByHandAsync();
        await VitaraDbContext.CreateMissingTablesAsync(db);
        await VitaraDbContext.CreateMissingTablesAsync(db);

        Assert.Equal(0, await db.Observations.CountAsync());
    }

    [Fact]
    public async Task ExistingVitaraDataSurvivesTheNewTables()
    {
        // The spec's non-negotiable: migrations only, never drop and recreate.
        await using var db = await ExistingDbPatchedByHandAsync();

        db.Sleep.Add(new SleepSession { Id = "s1", Day = new DateOnly(2026, 9, 1), Score = 82 });
        await db.SaveChangesAsync();
        await VitaraDbContext.CreateMissingTablesAsync(db);

        Assert.Equal(82, (await db.Sleep.SingleAsync()).Score);
    }
}
