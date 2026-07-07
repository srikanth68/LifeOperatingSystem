using Karma.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Karma.Infrastructure.Data;

public class KarmaDbContext(DbContextOptions<KarmaDbContext> options) : DbContext(options)
{
    public DbSet<Habit> Habits => Set<Habit>();
    public DbSet<HabitLog> HabitLogs => Set<HabitLog>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<GoalMilestone> GoalMilestones => Set<GoalMilestone>();

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
