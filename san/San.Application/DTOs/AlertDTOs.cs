namespace San.Application.DTOs;

public record AlertResult(
    Guid Id, string Type, string Title, string Description,
    decimal? ThresholdValue, DateTime? TriggerAt, bool Active,
    bool NotifyTelegram, DateTime? TriggeredAt, DateTime CreatedAt);

public record AlertUpsertRequest(
    string Type, string Title, string Description,
    decimal? ThresholdValue, DateTime? TriggerAt, bool Active, bool NotifyTelegram);
