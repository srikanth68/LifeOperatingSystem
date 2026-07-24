using NorthStar.Domain.Entities;

namespace NorthStar.Application.Interfaces;

public interface INorthStarRepository
{
    // Knowledge
    Task<KnowledgeEntry> AddEntryAsync(KnowledgeEntry entry);
    Task<KnowledgeEntry> UpsertDailyEntryAsync(string source, string topic, string summary, string rawJson, DateOnly day);
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

    // Module snapshots
    Task UpsertSnapshotAsync(ModuleSnapshot snapshot);
    Task<ModuleSnapshot?> GetSnapshotAsync(string module);
    Task<List<ModuleSnapshot>> GetAllSnapshotsAsync();

    // User facts (persistent profile knowledge)
    Task UpsertFactAsync(UserFact fact);
    Task<UserFact?> GetFactAsync(string key);
    Task<List<UserFact>> GetAllFactsAsync();
    Task<bool> DeleteFactAsync(string key);

    // Activity events (append-only, event-level, real occurrence timestamps)
    Task<bool> AddEventIfNewAsync(ActivityEvent ev);              // false = duplicate EventKey, skipped
    Task<int> AddEventsIfNewAsync(IEnumerable<ActivityEvent> evs); // returns count actually inserted
    Task<List<ActivityEvent>> GetEventsSinceAsync(DateTime sinceUtc, string? source = null, int limit = 200);

    // Agent memory (FTS5-ranked long-term store)
    Task<MemoryEntry> SaveMemoryAsync(MemoryEntry memory);
    Task<List<MemoryEntry>> RecallMemoriesAsync(string query, string? kind = null, int limit = 10);
    Task<List<MemoryEntry>> GetRecentMemoriesAsync(int limit = 20, string? kind = null);
    Task<bool> DeleteMemoryAsync(Guid id);
    Task<(int Total, Dictionary<string, int> ByKind)> GetMemoryStatsAsync();
}
