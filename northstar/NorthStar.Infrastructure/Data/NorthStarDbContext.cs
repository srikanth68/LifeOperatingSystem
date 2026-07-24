using Microsoft.EntityFrameworkCore;
using NorthStar.Domain.Entities;

namespace NorthStar.Infrastructure.Data;

public class NorthStarDbContext(DbContextOptions<NorthStarDbContext> options) : DbContext(options)
{
    public DbSet<KnowledgeEntry> Entries => Set<KnowledgeEntry>();
    public DbSet<Insight> Insights => Set<Insight>();
    public DbSet<ModuleSync> ModuleSyncs => Set<ModuleSync>();
    public DbSet<ActionItem> Actions => Set<ActionItem>();
    public DbSet<ModuleSnapshot> Snapshots => Set<ModuleSnapshot>();
    public DbSet<UserFact> Facts => Set<UserFact>();
    public DbSet<MemoryEntry> Memories => Set<MemoryEntry>();
    public DbSet<ActivityEvent> Events => Set<ActivityEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<KnowledgeEntry>(e =>
        {
            e.HasKey(k => k.Id);
            e.HasIndex(k => k.Source);
            e.HasIndex(k => k.Topic);
            e.HasIndex(k => k.Day);
            e.HasIndex(k => k.CreatedAt);
            e.Property(k => k.Day).HasConversion(
                d => d.HasValue ? d.Value.ToString("yyyy-MM-dd") : null,
                s => s != null ? DateOnly.Parse(s) : null);
        });

        b.Entity<Insight>(e =>
        {
            e.HasKey(i => i.Id);
            e.HasIndex(i => i.CreatedAt);
        });

        b.Entity<ModuleSync>(e => e.HasKey(m => m.Module));

        b.Entity<ActionItem>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.Status);
            e.HasIndex(a => a.Priority);
            e.HasIndex(a => a.DueDate);
            e.Property(a => a.DueDate).HasConversion(
                d => d.HasValue ? d.Value.ToString("yyyy-MM-dd") : null,
                s => s != null ? DateOnly.Parse(s) : null);
        });

        b.Entity<ModuleSnapshot>(e => e.HasKey(m => m.Module));
        b.Entity<UserFact>(e => e.HasKey(f => f.Key));

        b.Entity<MemoryEntry>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.Kind);
            e.HasIndex(m => m.CreatedAt);
            e.HasIndex(m => m.Importance);
        });

        b.Entity<ActivityEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.EventKey).IsUnique(); // idempotent ingestion
            e.HasIndex(x => x.OccurredAt);          // "since <ts>" range scans
            e.HasIndex(x => x.Source);
        });
    }
}
