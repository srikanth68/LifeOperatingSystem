namespace San.Application.DTOs;

public record ActivityFeedEntry(string Module, string Title, string Description, DateTime OccurredAt);

public record ModuleStatus(string Module, bool Reachable, string? Error);

public record FeedResult(List<ActivityFeedEntry> Entries, List<ModuleStatus> Modules);
