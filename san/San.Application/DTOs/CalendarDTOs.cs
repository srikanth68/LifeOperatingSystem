namespace San.Application.DTOs;

// StartTimeLocal / EndTimeLocal for the same reason reminders carry DueAtLocal. An
// event misreported by the UTC offset is worse here than anywhere: a meeting named at
// the wrong hour is a meeting missed.
public record CalendarEventResult(
    Guid Id, string Title, string? Description, DateTime StartTime, DateTime EndTime,
    string? Location, string Source, string? ExternalId, string? CalendarName,
    bool AllDay, DateTime CreatedAt, DateTime UpdatedAt,
    string StartTimeLocal, string EndTimeLocal);

public record CalendarEventUpsertRequest(
    string Title, string? Description, DateTime StartTime, DateTime EndTime,
    string? Location, bool AllDay);

public record NowNextResult(
    CalendarEventResult? Current, List<CalendarEventResult> Upcoming, DateTime AsOf);
