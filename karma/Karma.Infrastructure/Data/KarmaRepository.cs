using Karma.Application.Interfaces;
using Karma.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Karma.Infrastructure.Data;

public class KarmaRepository(KarmaDbContext db) : IKarmaRepository
{
    // ── Habits ───────────────────────────────────────────────
    public Task<List<Habit>> GetHabitsAsync(bool activeOnly = false) =>
        db.Habits
          .Where(h => !activeOnly || h.IsActive)
          .OrderBy(h => h.CreatedAt)
          .ToListAsync();

    public Task<Habit?> GetHabitAsync(Guid id) =>
        db.Habits.FirstOrDefaultAsync(h => h.Id == id);

    public async Task<Habit> AddHabitAsync(Habit habit)
    {
        db.Habits.Add(habit);
        await db.SaveChangesAsync();
        return habit;
    }

    public async Task<Habit?> UpdateHabitAsync(Guid id, Action<Habit> apply)
    {
        var habit = await db.Habits.FindAsync(id);
        if (habit is null) return null;
        apply(habit);
        await db.SaveChangesAsync();
        return habit;
    }

    public async Task<bool> DeleteHabitAsync(Guid id)
    {
        var habit = await db.Habits.FindAsync(id);
        if (habit is null) return false;
        db.Habits.Remove(habit);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Habit Logs ───────────────────────────────────────────
    public Task<HabitLog?> GetHabitLogAsync(Guid habitId, DateOnly date) =>
        db.HabitLogs.FirstOrDefaultAsync(l => l.HabitId == habitId && l.Date == date);

    public Task<List<HabitLog>> GetHabitLogsAsync(Guid habitId, DateOnly from, DateOnly to) =>
        db.HabitLogs
          .Where(l => l.HabitId == habitId && l.Date >= from && l.Date <= to)
          .OrderBy(l => l.Date)
          .ToListAsync();

    public Task<List<HabitLog>> GetLogsForDateAsync(DateOnly date) =>
        db.HabitLogs.Where(l => l.Date == date).ToListAsync();

    public async Task<HabitLog> UpsertHabitLogAsync(Guid habitId, DateOnly date, bool completed, string? note)
    {
        var log = await db.HabitLogs.FirstOrDefaultAsync(l => l.HabitId == habitId && l.Date == date);
        if (log is null)
        {
            log = new HabitLog { HabitId = habitId, Date = date, Completed = completed, Note = note };
            db.HabitLogs.Add(log);
        }
        else
        {
            log.Completed = completed;
            log.Note = note;
            log.LoggedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return log;
    }

    public async Task MarkHabitNotifiedAsync(Guid habitId, DateOnly date)
    {
        var habit = await db.Habits.FindAsync(habitId);
        if (habit is not null)
        {
            habit.LastNotificationSentOn = date;
            await db.SaveChangesAsync();
        }
    }

    // ── Goals ────────────────────────────────────────────────
    public Task<List<Goal>> GetGoalsAsync(string? status = null, string? category = null) =>
        db.Goals
          .Where(g => (status == null || g.Status == status) && (category == null || g.Category == category))
          .OrderByDescending(g => g.CreatedAt)
          .ToListAsync();

    public Task<Goal?> GetGoalAsync(Guid id) =>
        db.Goals.FirstOrDefaultAsync(g => g.Id == id);

    public async Task<Goal> AddGoalAsync(Goal goal)
    {
        db.Goals.Add(goal);
        await db.SaveChangesAsync();
        return goal;
    }

    public async Task<Goal?> UpdateGoalAsync(Guid id, Action<Goal> apply)
    {
        var goal = await db.Goals.FindAsync(id);
        if (goal is null) return null;
        apply(goal);
        await db.SaveChangesAsync();
        return goal;
    }

    public async Task<bool> DeleteGoalAsync(Guid id)
    {
        var goal = await db.Goals.FindAsync(id);
        if (goal is null) return false;
        db.Goals.Remove(goal);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Milestones ───────────────────────────────────────────
    public Task<List<GoalMilestone>> GetMilestonesAsync(Guid goalId) =>
        db.GoalMilestones
          .Where(m => m.GoalId == goalId)
          .OrderBy(m => m.CreatedAt)
          .ToListAsync();

    public async Task<GoalMilestone> AddMilestoneAsync(GoalMilestone milestone)
    {
        db.GoalMilestones.Add(milestone);
        await db.SaveChangesAsync();
        return milestone;
    }

    public async Task<GoalMilestone?> UpdateMilestoneAsync(Guid id, Action<GoalMilestone> apply)
    {
        var m = await db.GoalMilestones.FindAsync(id);
        if (m is null) return null;
        apply(m);
        await db.SaveChangesAsync();
        return m;
    }

    public async Task<bool> DeleteMilestoneAsync(Guid id)
    {
        var m = await db.GoalMilestones.FindAsync(id);
        if (m is null) return false;
        db.GoalMilestones.Remove(m);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Streak helpers ───────────────────────────────────────
    public static (int current, int best) ComputeStreaks(List<HabitLog> logs, DateOnly today)
    {
        var completed = logs.Where(l => l.Completed).Select(l => l.Date).ToHashSet();
        if (completed.Count == 0) return (0, 0);

        // current streak — consecutive days ending on today (or yesterday if today not logged yet)
        int current = 0;
        var d = completed.Contains(today) ? today : today.AddDays(-1);
        while (completed.Contains(d)) { current++; d = d.AddDays(-1); }

        // best streak
        int best = 0, run = 0;
        DateOnly? prev = null;
        foreach (var date in completed.OrderBy(x => x))
        {
            if (prev.HasValue && date == prev.Value.AddDays(1)) run++;
            else run = 1;
            if (run > best) best = run;
            prev = date;
        }
        return (current, best);
    }
}
