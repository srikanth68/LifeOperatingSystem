namespace San.Application.DTOs;

public record ReminderResult(
    Guid Id, string Text, DateTime DueAt, bool Done,
    bool NotifyTelegram, DateTime? NotifiedAt, DateTime CreatedAt);

public record ReminderUpsertRequest(string Text, DateTime DueAt, bool NotifyTelegram, bool? Done);
