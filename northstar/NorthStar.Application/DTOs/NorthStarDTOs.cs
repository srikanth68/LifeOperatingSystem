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

// ── Activity events ──
// OccurredAt is the REAL time the event happened (module-supplied). EventKey is the
// producer's stable idempotency key ("vault:transaction:<id>"); if omitted, the server
// derives one so retries still can't duplicate.
public record EventRequest(
    string Source, string Kind, string Title,
    string? Detail = null, DateTime? OccurredAt = null, string? EventKey = null, string? RawJson = null);

public record EventBatchRequest(List<EventRequest> Events);

public record EventResult(
    Guid Id, string Source, string Kind, string Title, string? Detail,
    DateTime OccurredAt, DateTime RecordedAt);
