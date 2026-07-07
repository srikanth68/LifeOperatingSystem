using Karma.Domain.Entities;

namespace Karma.Application.Interfaces;

public interface IKarmaRepository
{
    // Habits
    Task<List<Habit>> GetHabitsAsync(bool activeOnly = false);
    Task<Habit?> GetHabitAsync(Guid id);
    Task<Habit> AddHabitAsync(Habit habit);
    Task<Habit?> UpdateHabitAsync(Guid id, Action<Habit> apply);
    Task<bool> DeleteHabitAsync(Guid id);

    // Habit logs
    Task<HabitLog?> GetHabitLogAsync(Guid habitId, DateOnly date);
    Task<List<HabitLog>> GetHabitLogsAsync(Guid habitId, DateOnly from, DateOnly to);
    Task<List<HabitLog>> GetLogsForDateAsync(DateOnly date);
    Task<HabitLog> UpsertHabitLogAsync(Guid habitId, DateOnly date, bool completed, string? note);
    Task MarkHabitNotifiedAsync(Guid habitId, DateOnly date);

    // Goals
    Task<List<Goal>> GetGoalsAsync(string? status = null, string? category = null);
    Task<Goal?> GetGoalAsync(Guid id);
    Task<Goal> AddGoalAsync(Goal goal);
    Task<Goal?> UpdateGoalAsync(Guid id, Action<Goal> apply);
    Task<bool> DeleteGoalAsync(Guid id);

    // Milestones
    Task<List<GoalMilestone>> GetMilestonesAsync(Guid goalId);
    Task<GoalMilestone> AddMilestoneAsync(GoalMilestone milestone);
    Task<GoalMilestone?> UpdateMilestoneAsync(Guid id, Action<GoalMilestone> apply);
    Task<bool> DeleteMilestoneAsync(Guid id);
}
