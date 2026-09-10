namespace San.Application.DTOs;

// DueAtLocal exists because San told the user a reminder was set for 2:30pm when it
// had created it for 10:30am -- this timezone's offset, exactly. The instant was
// right; only the reading of it was wrong. See LocalTimeText.
public record ReminderResult(
    Guid Id, string Text, DateTime DueAt, bool Done,
    bool NotifyTelegram, DateTime? NotifiedAt, DateTime CreatedAt,
    string DueAtLocal);

public record ReminderUpsertRequest(string Text, DateTime DueAt, bool NotifyTelegram, bool? Done);
