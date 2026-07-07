namespace San.Application.DTOs;

public record CalendarEventResult(
    Guid Id, string Title, string? Description, DateTime StartTime, DateTime EndTime,
    string? Location, string Source, string? ExternalId, string? CalendarName,
    bool AllDay, DateTime CreatedAt, DateTime UpdatedAt);

public record CalendarEventUpsertRequest(
    string Title, string? Description, DateTime StartTime, DateTime EndTime,
    string? Location, bool AllDay);

public record NowNextResult(
    CalendarEventResult? Current, List<CalendarEventResult> Upcoming, DateTime AsOf);
