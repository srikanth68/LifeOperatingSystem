using NorthStar.Application.DTOs;
using NorthStar.Domain.Entities;

namespace NorthStar.Application.Interfaces;

public interface INorthStarRepository
{
    // Knowledge
    Task<KnowledgeEntry> AddEntryAsync(KnowledgeEntry entry);
    Task<(KnowledgeEntry Entry, bool Created)> UpsertDailyEntryAsync(
        string source, string topic, string summary, string? rawJson, DateOnly day);
    Task<List<KnowledgeEntry>> GetEntriesAsync(string? source = null, string? topic = null, int days = 30, int limit = 200);
    Task<KnowledgeEntry?> GetEntryAsync(Guid id);
    Task<List<KnowledgeEntry>> SearchAsync(string query, int limit = 50);
    Task<int> GetEntryCountAsync(string? source = null);

    // Insights
    Task<Insight> AddInsightAsync(Insight insight);
    Task<List<Insight>> GetInsightsAsync(bool includeDismissed = false, int limit = 50);
    Task<Insight?> DismissInsightAsync(Guid id);

    // Timeline
    Task<List<KnowledgeEntry>> GetTimelineAsync(int days = 7, int limit = 100);

    // Module sync tracking
    Task<ModuleSync?> GetModuleSyncAsync(string module);
    Task UpsertModuleSyncAsync(ModuleSync sync);
    Task<List<ModuleSync>> GetAllModuleSyncsAsync();

    // Actions
    Task<ActionItem> AddActionAsync(ActionItem action);
    Task<List<ActionItem>> GetActionsAsync(string? status = "pending", int limit = 50);
    Task<ActionItem?> UpdateActionAsync(Guid id, string status, string? resolvedBy = null);
    Task<bool> DeleteActionAsync(Guid id);

    // Module snapshots
    Task UpsertSnapshotAsync(ModuleSnapshot snapshot);
    Task<ModuleSnapshot?> GetSnapshotAsync(string module);
    Task<List<ModuleSnapshot>> GetAllSnapshotsAsync();

    // User facts (persistent profile knowledge)
    Task UpsertFactAsync(UserFact fact);
    Task<UserFact?> GetFactAsync(string key);
    Task<List<UserFact>> GetAllFactsAsync();
    Task<bool> DeleteFactAsync(string key);
    Task<List<FactRevision>> GetFactHistoryAsync(string key);   // previous values, newest first

    // Activity events (append-only, event-level, real occurrence timestamps)
    Task<bool> AddEventIfNewAsync(ActivityEvent ev);              // false = duplicate EventKey, skipped
    Task<int> AddEventsIfNewAsync(IEnumerable<ActivityEvent> evs); // returns count actually inserted
    Task<ActivityEvent?> GetEventByKeyAsync(string eventKey);
    Task<List<ActivityEvent>> GetEventsSinceAsync(DateTime sinceUtc, string? source = null, int limit = 200);
    // Recent events from one source whose Detail mentions any of the needles (an address, a thread id).
    Task<List<ActivityEvent>> FindEventsAsync(string source, IReadOnlyList<string> detailContainsAny, int days = 365, int limit = 5);

    // Agent memory (FTS5-ranked long-term store, with a lifecycle -- see MemoryStatus)
    Task<MemoryEntry> SaveMemoryAsync(MemoryEntry memory);   // raw insert, no rules
    Task<MemoryWriteResult> RememberAsync(MemoryEntry draft, IReadOnlyList<string>? entityNames = null);
    Task<(MemoryEntry? Memory, string? Error)> ConfirmMemoryAsync(Guid id, string? note = null);
    Task<(MemoryEntry? Memory, string? Error)> RetractMemoryAsync(Guid id, string? reason = null);
    Task<MemoryDetail?> GetMemoryDetailAsync(Guid id);
    Task<List<MemoryEntry>> RecallMemoriesAsync(string query, string? kind = null, int limit = 10);
    Task<List<MemoryEntry>> GetRecentMemoriesAsync(int limit = 20, string? kind = null, string? status = null);
    Task<bool> DeleteMemoryAsync(Guid id);
    Task<(int Total, Dictionary<string, int> ByKind, Dictionary<string, int> ByStatus)> GetMemoryStatsAsync();
    Task<int> EmbedPendingAsync(int batch = 32, CancellationToken ct = default);   // meaning-search backfill

    // Entities and links between records
    Task<NamedEntity?> ResolveEntityAsync(string name);
    Task<(NamedEntity? Entity, bool Created, string? Error)> UpsertEntityAsync(
        string name, string? type = null, string? aliases = null, string? note = null);
    Task<(NamedEntity? Entity, string? Error)> UpdateEntityAsync(Guid id, string? name, string? type, string? aliases, string? note);
    Task<bool> DeleteEntityAsync(Guid id);
    Task<List<NamedEntity>> FindEntitiesAsync(string? query, string? type = null, int limit = 50);
    Task<List<NamedEntity>> EntitiesNamedInAsync(string text);
    Task<EntityDetail?> GetEntityDetailAsync(Guid id);
    Task<(RecordLink? Link, bool Created, string? Error)> AddLinkAsync(RecordLink link);
    Task<bool> DeleteLinkAsync(Guid id);
    Task<List<LinkedRecord>> GetLinksAsync(string type, string id);
    Task<string?> DescribeAsync(string type, string id);
}
