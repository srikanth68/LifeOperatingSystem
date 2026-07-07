namespace San.Application.DTOs;

public record ContextPushRequest(
    LocationPayload? Location,
    List<CalendarEventPayload>? CalendarEvents,
    HealthPayload? Health,
    DateTime Timestamp);

public record LocationPayload(double Latitude, double Longitude, string? Address);

public record CalendarEventPayload(
    string Title, DateTime StartTime, DateTime EndTime, string? Location, bool AllDay);

public record HealthPayload(
    int? Steps, int? HeartRate, int? ActiveCalories, double? SleepHours, string? RawJson);

public record ContextPushResult(bool Received, string Message);
