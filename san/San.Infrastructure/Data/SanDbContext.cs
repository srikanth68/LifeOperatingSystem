using Microsoft.EntityFrameworkCore;
using San.Domain.Entities;

namespace San.Infrastructure.Data;

public class SanDbContext(DbContextOptions<SanDbContext> options) : DbContext(options)
{
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<Alert> Alerts => Set<Alert>();

    protected override void OnModelCreating(ModelBuilder b)
    {
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
    }
}
