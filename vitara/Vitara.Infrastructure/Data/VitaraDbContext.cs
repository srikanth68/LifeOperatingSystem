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

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
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
