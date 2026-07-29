using Karma.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Karma.Infrastructure.Data;

public class KarmaDbContext(DbContextOptions<KarmaDbContext> options) : DbContext(options)
{
    public DbSet<Habit> Habits => Set<Habit>();
    public DbSet<HabitLog> HabitLogs => Set<HabitLog>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<GoalMilestone> GoalMilestones => Set<GoalMilestone>();

    // SQLite has no timezone-aware column type — every DateTime loses its Kind on
    // round-trip and comes back Unspecified, which System.Text.Json then serializes
    // without a 'Z' suffix, causing frontend clients to misparse UTC instants as
    // local time. All DateTime columns here are UTC instants — re-tag Kind=Utc on read.
    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class UtcNullableDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Habit>(e =>
        {
            e.HasKey(h => h.Id);
            e.Ignore(h => h.NotifyDays);
        });

        b.Entity<HabitLog>(e =>
        {
            e.HasKey(l => l.Id);
            e.HasIndex(l => new { l.HabitId, l.Date }).IsUnique();
        });

        b.Entity<Goal>(e => e.HasKey(g => g.Id));

        b.Entity<GoalMilestone>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.GoalId);
        });
    }
}
