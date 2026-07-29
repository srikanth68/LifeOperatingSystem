using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using San.Domain.Entities;

namespace San.Infrastructure.Data;

public class SanDbContext(DbContextOptions<SanDbContext> options) : DbContext(options)
{
    // SQLite has no timezone-aware column type — every DateTime loses its Kind on
    // round-trip and comes back Unspecified. System.Text.Json then serializes it
    // without a 'Z' suffix, so the frontend's `new Date(iso)` silently misparses it
    // as browser-local time instead of UTC (manifested as reminders displaying hours
    // off — exactly the local UTC offset). All DateTime columns here are UTC instants
    // (DueAt, CreatedAt, etc.), so re-tag Kind=Utc on every read.
    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class UtcNullableDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();
    public DbSet<LocationUpdate> LocationUpdates => Set<LocationUpdate>();
    public DbSet<ActivitySnapshot> ActivitySnapshots => Set<ActivitySnapshot>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<EmailAccount> EmailAccounts => Set<EmailAccount>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppSetting>(e => e.HasKey(s => s.Key));

        b.Entity<ChatMessage>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.CreatedAt);
        });

        b.Entity<Reminder>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.DueAt).IsRequired();
        });

        b.Entity<Alert>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.ThresholdValue).HasColumnType("decimal(18,2)");
        });

        b.Entity<CalendarEvent>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.StartTime);
            e.HasIndex(c => new { c.ExternalId, c.Source });
        });

        b.Entity<LocationUpdate>(e =>
        {
            e.HasKey(l => l.Id);
            e.HasIndex(l => l.Timestamp);
        });

        b.Entity<ActivitySnapshot>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.Timestamp);
        });

        b.Entity<Person>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Name);
            e.HasIndex(p => p.Birthday);
        });

        b.Entity<EmailAccount>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => new { a.Provider, a.EmailAddress }).IsUnique();
        });
    }
}
