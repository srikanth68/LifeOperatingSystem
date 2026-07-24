using Microsoft.EntityFrameworkCore;
using San.Domain.Entities;

namespace San.Infrastructure.Data;

public class SanDbContext(DbContextOptions<SanDbContext> options) : DbContext(options)
{
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();
    public DbSet<LocationUpdate> LocationUpdates => Set<LocationUpdate>();
    public DbSet<ActivitySnapshot> ActivitySnapshots => Set<ActivitySnapshot>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

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
    }
}
