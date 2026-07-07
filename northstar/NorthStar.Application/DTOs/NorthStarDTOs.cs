namespace NorthStar.Application.DTOs;

public record IngestRequest(string Source, string Topic, string Summary, string? RawJson = null, string? Day = null);

public record IngestBatchRequest(List<IngestRequest> Entries);

public record KnowledgeEntryResult(Guid Id, string Source, string Topic, string Summary, string? Day, DateTime CreatedAt);

public record InsightResult(Guid Id, string Title, string Body, string GeneratedBy, bool Dismissed, DateTime CreatedAt);

public record TimelineResult(List<KnowledgeEntryResult> Entries, Dictionary<string, int> SourceCounts, int TotalEntries);

public record DashboardResult(
    int TotalEntries,
    Dictionary<string, int> EntriesBySource,
    Dictionary<string, int> EntriesByTopic,
    List<InsightResult> RecentInsights,
    List<KnowledgeEntryResult> RecentEntries,
    Dictionary<string, DateTime?> LastSyncByModule
);

public record InsightCreateRequest(string Title, string Body);

public record SearchResult(List<KnowledgeEntryResult> Entries, int Count, string Query);
