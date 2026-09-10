namespace San.Application.DTOs;

// TriggerAtLocal for the same reason reminders carry DueAtLocal: a bare UTC instant
// with no Z gets read as wall-clock time by both the model and the browser.
public record AlertResult(
    Guid Id, string Type, string Title, string Description,
    decimal? ThresholdValue, DateTime? TriggerAt, bool Active,
    bool NotifyTelegram, DateTime? TriggeredAt, DateTime CreatedAt,
    string? TriggerAtLocal);

public record AlertUpsertRequest(
    string Type, string Title, string Description,
    decimal? ThresholdValue, DateTime? TriggerAt, bool Active, bool NotifyTelegram);
