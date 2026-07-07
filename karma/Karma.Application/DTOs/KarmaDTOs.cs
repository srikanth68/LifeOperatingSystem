namespace Karma.Application.DTOs;

// ── Habits ───────────────────────────────────────────────────
public record HabitRequest(
    string Name,
    string? Description,
    string Emoji,
    string Category,
    string? NotifyTime,
    string? NotifyMessage,
    string? NotifyChannel,
    int[]? NotifyDays
);

public record HabitLogRequest(DateOnly? Date, bool Completed, string? Note);

public record HabitResult(
    Guid Id,
    string Name,
    string? Description,
    string Emoji,
    string Category,
    string? NotifyTime,
    string? NotifyMessage,
    string NotifyChannel,
    int[] NotifyDays,
    bool IsActive,
    int CurrentStreak,
    int BestStreak,
    bool? TodayCompleted,
    DateTime CreatedAt
);

public record HabitLogResult(Guid Id, Guid HabitId, DateOnly Date, bool Completed, string? Note, DateTime LoggedAt);

// ── Goals ────────────────────────────────────────────────────
public record GoalLink(string Label, string Url);

public record GoalRequest(
    string Title,
    string? Description,
    string Category,
    string? Status,
    int? Progress,
    DateOnly? TargetDate,
    List<GoalLink>? Links,
    string? Resources,
    string? Tags
);

public record MilestoneRequest(string Title, DateOnly? TargetDate);

public record MilestoneResult(Guid Id, string Title, DateOnly? TargetDate, bool Completed, DateTime? CompletedAt);

public record GoalResult(
    Guid Id,
    string Title,
    string? Description,
    string Category,
    string Status,
    int Progress,
    DateOnly? TargetDate,
    List<GoalLink> Links,
    string? Resources,
    string? Tags,
    List<MilestoneResult> Milestones,
    DateTime CreatedAt,
    DateTime? CompletedAt
);

// ── Notifications ─────────────────────────────────────────────
public record NotificationRequest(string Message, string? Channel);
