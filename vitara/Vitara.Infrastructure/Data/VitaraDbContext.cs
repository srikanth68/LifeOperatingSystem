using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Vitara.Domain.Entities;

namespace Vitara.Infrastructure.Data;

public class VitaraDbContext(DbContextOptions<VitaraDbContext> options) : DbContext(options)
{
    public const string DayFormat = "yyyy-MM-dd";

    // Separate bug: SQLite has no timezone-aware column type, so every DateTime (not
    // DateOnly Day fields — those are handled below) loses its Kind on round-trip and
    // comes back Unspecified, which System.Text.Json then serializes without a 'Z'
    // suffix, causing frontend clients to misparse UTC instants as local time.
    // HeartRateSample.Timestamp, Workout.StartTime/EndTime, etc. are UTC instants —
    // re-tag Kind=Utc on read.
    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class UtcNullableDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    // ISO-8601 text, which sorts correctly as TEXT in SQLite. The existing entities
    // configure their Day property individually; these conventions cover the health
    // tables, which carry thirteen DateOnly columns between them and would otherwise
    // need the same three lines repeated for each.
    private sealed class DayConverter() : ValueConverter<DateOnly, string>(
        d => d.ToString(DayFormat, CultureInfo.InvariantCulture),
        s => DateOnly.ParseExact(s, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None));

    private sealed class NullableDayConverter() : ValueConverter<DateOnly?, string?>(
        d => d.HasValue ? d.Value.ToString(DayFormat, CultureInfo.InvariantCulture) : null,
        s => s == null ? null : DateOnly.ParseExact(s, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None));

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
        configurationBuilder.Properties<DateOnly>().HaveConversion<DayConverter>();
        configurationBuilder.Properties<DateOnly?>().HaveConversion<NullableDayConverter>();
    }

    public DbSet<OuraToken>              Tokens          => Set<OuraToken>();
    public DbSet<UserProfile>            Profiles        => Set<UserProfile>();
    public DbSet<SleepSession>           Sleep           => Set<SleepSession>();
    public DbSet<DailyReadiness>         Readiness       => Set<DailyReadiness>();
    public DbSet<DailyActivity>          Activity        => Set<DailyActivity>();
    public DbSet<DailyStress>            Stress          => Set<DailyStress>();
    public DbSet<DailyResilience>        Resilience      => Set<DailyResilience>();
    public DbSet<DailyCardiovascularAge> CardiovascularAge => Set<DailyCardiovascularAge>();
    public DbSet<DailySpo2>              Spo2            => Set<DailySpo2>();
    public DbSet<HeartRateSample>        HeartRate       => Set<HeartRateSample>();
    public DbSet<Vo2MaxRecord>           Vo2Max          => Set<Vo2MaxRecord>();
    public DbSet<Workout>                Workouts        => Set<Workout>();
    public DbSet<DailyNutrition>         Nutrition       => Set<DailyNutrition>();
    public DbSet<MealEntry>              Meals           => Set<MealEntry>();
    public DbSet<SyncState>              SyncStates      => Set<SyncState>();

    // ── Health intelligence ──
    // Additive: the typed tables above stay the source of truth for ingest and display.
    public DbSet<Observation>            Observations    => Set<Observation>();
    public DbSet<Device>                 Devices         => Set<Device>();
    public DbSet<Intervention>           Interventions   => Set<Intervention>();
    public DbSet<ExcludedPeriod>         ExcludedPeriods => Set<ExcludedPeriod>();
    public DbSet<TravelPeriod>           TravelPeriods   => Set<TravelPeriod>();
    public DbSet<LabPanel>               LabPanels       => Set<LabPanel>();
    public DbSet<ReferenceRange>         ReferenceRanges => Set<ReferenceRange>();
    public DbSet<Baseline>               Baselines       => Set<Baseline>();
    public DbSet<DerivedMetric>          DerivedMetrics  => Set<DerivedMetric>();
    public DbSet<Finding>                Findings        => Set<Finding>();
    public DbSet<Measurement>            Measurements    => Set<Measurement>();
    public DbSet<MetricCorrelation>      Correlations    => Set<MetricCorrelation>();
    public DbSet<WeighIn>                WeighIns        => Set<WeighIn>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<OuraToken>().HasKey(t => t.Id);
        b.Entity<UserProfile>().HasKey(u => u.Id);

        ConfigureDayEntity<SleepSession>(b, s => s.Id, s => s.Day);
        ConfigureDayEntity<DailyReadiness>(b, r => r.Id, r => r.Day);
        ConfigureDayEntity<DailyActivity>(b, a => a.Id, a => a.Day);
        ConfigureDayEntity<DailyStress>(b, s => s.Id, s => s.Day);
        ConfigureDayEntity<DailyResilience>(b, r => r.Id, r => r.Day);
        ConfigureDayEntity<DailyCardiovascularAge>(b, c => c.Id, c => c.Day);
        ConfigureDayEntity<DailySpo2>(b, s => s.Id, s => s.Day);
        ConfigureDayEntity<Vo2MaxRecord>(b, v => v.Id, v => v.Day);
        ConfigureDayEntity<Workout>(b, w => w.Id, w => w.Day);
        ConfigureDayEntity<DailyNutrition>(b, n => n.Id, n => n.Day);
        ConfigureDayEntity<WeighIn>(b, w => w.Id, w => w.Day);

        b.Entity<SyncState>(e => e.HasKey(x => x.Source));

        b.Entity<Observation>(e =>
        {
            e.HasKey(x => x.Id);
            // Re-ingesting the same day must not duplicate rows. Source is part of the
            // key because the same metric can legitimately arrive from the ring and
            // from a manual entry on the same day, and both are real.
            e.HasIndex(x => new { x.Metric, x.ObservedAtLocal, x.Source }).IsUnique();
            // The query the analytics layer actually runs: one metric, one baseline
            // bucket, over a date window.
            e.HasIndex(x => new { x.Metric, x.BaselineSignature, x.ObservedDateLocal });
        });

        b.Entity<Device>(e => e.HasKey(x => x.Id));
        b.Entity<Intervention>(e => e.HasKey(x => x.Id));
        b.Entity<ExcludedPeriod>(e => e.HasKey(x => x.Id));
        b.Entity<TravelPeriod>(e => e.HasKey(x => x.Id));
        b.Entity<LabPanel>(e => e.HasKey(x => x.Id));
        b.Entity<ReferenceRange>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Metric); });

        b.Entity<Baseline>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Metric, x.BaselineSignature, x.ComputedOnLocal });
        });

        b.Entity<DerivedMetric>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Metric, x.ObservedDateLocal }).IsUnique();
        });

        b.Entity<Finding>(e =>
        {
            e.HasKey(x => x.Id);
            // One row per ongoing condition, not one per day it persists. The key is
            // what the ledger deduplicates on.
            e.HasIndex(x => x.Key);
            e.HasIndex(x => x.ResolvedLocal);
        });

        b.Entity<MealEntry>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.Day);
            e.HasIndex(m => m.MealType);
            e.Property(m => m.Day).HasConversion(
                d => d.ToString(DayFormat, System.Globalization.CultureInfo.InvariantCulture),
                s => DateOnly.ParseExact(s, DayFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None));
        });

        b.Entity<HeartRateSample>(e =>
        {
            e.HasKey(h => h.Id);
            e.Property(h => h.Id).ValueGeneratedOnAdd();
            e.HasIndex(h => h.Timestamp);
        });
    }

    public static async Task CreateMissingTablesAsync(VitaraDbContext db)
    {
        var sql = """
            CREATE TABLE IF NOT EXISTS Profiles (
                Id TEXT PRIMARY KEY,
                Age INTEGER,
                Weight REAL,
                Height REAL,
                BiologicalSex TEXT,
                Email TEXT,
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            );
            CREATE TABLE IF NOT EXISTS Stress (
                Id TEXT PRIMARY KEY,
                Day TEXT NOT NULL,
                StressHighSeconds INTEGER,
                RecoveryHighSeconds INTEGER,
                DaySummary TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_Stress_Day ON Stress(Day);
            CREATE TABLE IF NOT EXISTS Resilience (
                Id TEXT PRIMARY KEY,
                Day TEXT NOT NULL,
                Level TEXT,
                SleepRecovery INTEGER,
                DaytimeRecovery INTEGER,
                Stress INTEGER
            );
            CREATE INDEX IF NOT EXISTS IX_Resilience_Day ON Resilience(Day);
            CREATE TABLE IF NOT EXISTS CardiovascularAge (
                Id TEXT PRIMARY KEY,
                Day TEXT NOT NULL,
                VascularAge REAL
            );
            CREATE INDEX IF NOT EXISTS IX_CardiovascularAge_Day ON CardiovascularAge(Day);
            CREATE TABLE IF NOT EXISTS Spo2 (
                Id TEXT PRIMARY KEY,
                Day TEXT NOT NULL,
                Spo2Average REAL,
                BreathingDisturbanceIndex REAL
            );
            CREATE INDEX IF NOT EXISTS IX_Spo2_Day ON Spo2(Day);
            CREATE TABLE IF NOT EXISTS Vo2Max (
                Id TEXT PRIMARY KEY,
                Day TEXT NOT NULL,
                Vo2Max REAL
            );
            CREATE INDEX IF NOT EXISTS IX_Vo2Max_Day ON Vo2Max(Day);
            CREATE TABLE IF NOT EXISTS Workouts (
                Id TEXT PRIMARY KEY,
                Day TEXT NOT NULL,
                Activity TEXT NOT NULL DEFAULT '',
                StartTime TEXT,
                EndTime TEXT,
                Calories INTEGER,
                Distance INTEGER,
                Intensity TEXT,
                Label TEXT,
                Source TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_Workouts_Day ON Workouts(Day);
            CREATE TABLE IF NOT EXISTS HeartRate (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                Bpm INTEGER NOT NULL,
                Source TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_HeartRate_Timestamp ON HeartRate(Timestamp);
            CREATE TABLE IF NOT EXISTS Nutrition (
                Id TEXT PRIMARY KEY,
                Day TEXT NOT NULL,
                Calories INTEGER NOT NULL DEFAULT 0,
                Protein REAL NOT NULL DEFAULT 0,
                Carbs REAL NOT NULL DEFAULT 0,
                Fat REAL NOT NULL DEFAULT 0,
                Fiber REAL,
                Sugar REAL,
                Sodium REAL,
                CalorieGoal INTEGER,
                ProteinGoal REAL,
                CarbGoal REAL,
                FatGoal REAL,
                MealsJson TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_Nutrition_Day ON Nutrition(Day);
            CREATE TABLE IF NOT EXISTS Meals (
                Id TEXT PRIMARY KEY,
                Day TEXT NOT NULL,
                MealType TEXT NOT NULL DEFAULT 'snack',
                FoodName TEXT NOT NULL DEFAULT '',
                FdcId INTEGER,
                ServingQty REAL NOT NULL DEFAULT 1,
                ServingUnit TEXT,
                Calories REAL NOT NULL DEFAULT 0,
                Protein REAL NOT NULL DEFAULT 0,
                Carbs REAL NOT NULL DEFAULT 0,
                Fat REAL NOT NULL DEFAULT 0,
                Fiber REAL,
                LoggedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            );
            CREATE INDEX IF NOT EXISTS IX_Meals_Day ON Meals(Day);
            CREATE INDEX IF NOT EXISTS IX_Meals_MealType ON Meals(MealType);
            CREATE TABLE IF NOT EXISTS WeighIns (
                Id TEXT PRIMARY KEY,
                Day TEXT NOT NULL,
                WeightKg REAL NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            );
            CREATE INDEX IF NOT EXISTS IX_WeighIns_Day ON WeighIns(Day);
            """;
        await db.Database.ExecuteSqlRawAsync(sql);

        // Additive column migrations for existing DBs (EnsureCreated won't ALTER).
        await AddColumnIfMissingAsync(db, "Tokens", "LastSyncedAt", "TEXT");
        // Sync health. Without these a failed sync is indistinguishable from no sync at
        // all, and both look identical to a sync that worked.
        await AddColumnIfMissingAsync(db, "Tokens", "LastSyncAttemptAt", "TEXT");
        await AddColumnIfMissingAsync(db, "Tokens", "LastSyncError", "TEXT");

        await AddColumnIfMissingAsync(db, "Meals", "Source", "TEXT NOT NULL DEFAULT 'manual'");

        // ── Health intelligence tables ──
        //
        // EnsureCreated builds these from the model on a fresh database and does
        // nothing at all on one that already exists, which is every deployed box. So
        // they are created by hand here too, and the two definitions have to agree --
        // a column name that differs between them fails at query time, not at startup.
        //
        // These land now even though the nightly job that consumes most of them comes
        // later. Measurement context, a device swap, the week of flu: none of it can be
        // reconstructed afterwards from the readings. It is recorded at entry time or
        // it is gone.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS Observations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Metric TEXT NOT NULL DEFAULT '',
                Value REAL NOT NULL DEFAULT 0,
                Unit TEXT NOT NULL DEFAULT '',
                ObservedAtLocal TEXT NOT NULL,
                ObservedDateLocal TEXT NOT NULL,
                Tier TEXT NOT NULL DEFAULT 'dense',
                Source TEXT NOT NULL DEFAULT 'oura',
                SourceRecordId TEXT,
                DeviceId INTEGER,
                LabPanelId TEXT,
                ContextJson TEXT,
                BaselineSignature TEXT NOT NULL DEFAULT '',
                EligibleForBaseline INTEGER NOT NULL DEFAULT 1,
                ValueOriginal REAL,
                UnitOriginal TEXT,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Observations_Identity
                ON Observations (Metric, ObservedAtLocal, Source);
            CREATE INDEX IF NOT EXISTS IX_Observations_Window
                ON Observations (Metric, BaselineSignature, ObservedDateLocal);

            CREATE TABLE IF NOT EXISTS Devices (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Kind TEXT NOT NULL DEFAULT '',
                Model TEXT NOT NULL DEFAULT '',
                Firmware TEXT,
                ActiveFromLocal TEXT NOT NULL,
                ActiveToLocal TEXT,
                Notes TEXT
            );

            CREATE TABLE IF NOT EXISTS Interventions (
                Id TEXT PRIMARY KEY,
                Kind TEXT NOT NULL DEFAULT 'supplement',
                Name TEXT NOT NULL DEFAULT '',
                Dose TEXT,
                StartedOnLocal TEXT NOT NULL,
                EndedOnLocal TEXT,
                Notes TEXT,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            );

            CREATE TABLE IF NOT EXISTS ExcludedPeriods (
                Id TEXT PRIMARY KEY,
                StartLocal TEXT NOT NULL,
                EndLocal TEXT NOT NULL,
                Reason TEXT NOT NULL DEFAULT 'other',
                ExcludeFromBaseline INTEGER NOT NULL DEFAULT 1,
                Notes TEXT,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            );

            CREATE TABLE IF NOT EXISTS TravelPeriods (
                Id TEXT PRIMARY KEY,
                StartLocal TEXT NOT NULL,
                EndLocal TEXT NOT NULL,
                HomeTz TEXT NOT NULL DEFAULT 'America/New_York',
                AwayTz TEXT NOT NULL DEFAULT '',
                Notes TEXT
            );

            CREATE TABLE IF NOT EXISTS LabPanels (
                Id TEXT PRIMARY KEY,
                DrawnOnLocal TEXT NOT NULL,
                LabName TEXT,
                Notes TEXT,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            );

            CREATE TABLE IF NOT EXISTS ReferenceRanges (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Metric TEXT NOT NULL DEFAULT '',
                Low REAL,
                High REAL,
                Unit TEXT NOT NULL DEFAULT '',
                Sex TEXT,
                AgeMin INTEGER,
                AgeMax INTEGER,
                LabName TEXT,
                Notes TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_ReferenceRanges_Metric ON ReferenceRanges (Metric);

            CREATE TABLE IF NOT EXISTS Baselines (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Metric TEXT NOT NULL DEFAULT '',
                BaselineSignature TEXT NOT NULL DEFAULT '',
                ComputedOnLocal TEXT NOT NULL,
                WindowDays INTEGER NOT NULL DEFAULT 60,
                Mean REAL NOT NULL DEFAULT 0,
                StdDev REAL NOT NULL DEFAULT 0,
                Median REAL NOT NULL DEFAULT 0,
                P25 REAL NOT NULL DEFAULT 0,
                P75 REAL NOT NULL DEFAULT 0,
                N INTEGER NOT NULL DEFAULT 0,
                IsValid INTEGER NOT NULL DEFAULT 0,
                ExclusionsJson TEXT,
                RegimeStartLocal TEXT,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            );
            CREATE INDEX IF NOT EXISTS IX_Baselines_Lookup
                ON Baselines (Metric, BaselineSignature, ComputedOnLocal);

            CREATE TABLE IF NOT EXISTS DerivedMetrics (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Metric TEXT NOT NULL DEFAULT '',
                ObservedDateLocal TEXT NOT NULL,
                Value REAL NOT NULL DEFAULT 0,
                InputsJson TEXT,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_DerivedMetrics_Identity
                ON DerivedMetrics (Metric, ObservedDateLocal);

            CREATE TABLE IF NOT EXISTS Findings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Key TEXT NOT NULL DEFAULT '',
                Type TEXT NOT NULL DEFAULT '',
                Metric TEXT NOT NULL DEFAULT '',
                Direction TEXT NOT NULL DEFAULT '',
                Severity TEXT NOT NULL DEFAULT 'info',
                Confidence REAL,
                Summary TEXT NOT NULL DEFAULT '',
                EvidenceJson TEXT,
                FirstDetectedLocal TEXT NOT NULL,
                LastDetectedLocal TEXT NOT NULL,
                ResolvedLocal TEXT,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            );
            CREATE INDEX IF NOT EXISTS IX_Findings_Key ON Findings (Key);
            CREATE INDEX IF NOT EXISTS IX_Findings_Open ON Findings (ResolvedLocal);

            CREATE TABLE IF NOT EXISTS Measurements (
                Id TEXT PRIMARY KEY,
                Metric TEXT NOT NULL DEFAULT '',
                Value REAL NOT NULL DEFAULT 0,
                Unit TEXT NOT NULL DEFAULT '',
                ObservedAtLocal TEXT NOT NULL,
                Day TEXT NOT NULL,
                Tier TEXT NOT NULL DEFAULT 'medium',
                Source TEXT NOT NULL DEFAULT 'manual',
                ContextJson TEXT,
                Note TEXT,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            );
            CREATE INDEX IF NOT EXISTS IX_Measurements_Window
                ON Measurements (Metric, Day);

            -- Deliberately NOT unique on (Metric, Day). Several blood pressure readings
            -- in a day is normal and each is a real measurement; collapsing them would
            -- throw away the morning-versus-evening split that BaselineKeys exists for.
            -- Re-importing the same file is deduplicated on the instant instead.
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Measurements_Identity
                ON Measurements (Metric, ObservedAtLocal, Source);

            CREATE TABLE IF NOT EXISTS Correlations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Driver TEXT NOT NULL DEFAULT '',
                Outcome TEXT NOT NULL DEFAULT '',
                LagDays INTEGER NOT NULL DEFAULT 0,
                Rho REAL NOT NULL DEFAULT 0,
                N INTEGER NOT NULL DEFAULT 0,
                PValue REAL NOT NULL DEFAULT 1,
                WindowDays INTEGER NOT NULL DEFAULT 90,
                ComputedOnLocal TEXT NOT NULL,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Correlations_Identity
                ON Correlations (Driver, Outcome, LagDays, ComputedOnLocal);
            """);

        // Sync health for sources that have no token to hang it on.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS SyncStates (
                Source TEXT PRIMARY KEY,
                LastSyncedAt TEXT,
                LastAttemptAt TEXT,
                LastError TEXT
            );
            """);
    }

    private static async Task AddColumnIfMissingAsync(VitaraDbContext db, string table, string column, string type)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
        var exists = Convert.ToInt64(await check.ExecuteScalarAsync()) > 0;
        if (!exists)
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {column} {type}");
    }

    private void ConfigureDayEntity<T>(ModelBuilder b,
        System.Linq.Expressions.Expression<Func<T, object?>> keyExpr,
        System.Linq.Expressions.Expression<Func<T, DateOnly>> dayExpr) where T : class
    {
        b.Entity<T>(e =>
        {
            e.HasKey(keyExpr);
            e.Property(dayExpr).HasConversion(
                d => d.ToString(DayFormat, CultureInfo.InvariantCulture),
                s => DateOnly.ParseExact(s, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None));
        });
    }
}
