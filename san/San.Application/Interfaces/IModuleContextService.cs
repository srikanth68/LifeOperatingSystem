using San.Application.DTOs;

namespace San.Application.Interfaces;

// Pulls live data from the other Maaya modules (Vault, Vitara, Aasthi) over HTTP.
// Used to (a) ground San's chat answers in real numbers, and (b) build the activity feed.
// Must degrade gracefully — any module being offline should not break chat or the feed.
public interface IModuleContextService
{
    Task<string> BuildChatContextAsync(CancellationToken ct = default);
    Task<FeedResult> GetActivityFeedAsync(CancellationToken ct = default);

    // Time awareness for chat: current wall-clock time in the user's configured
    // timezone, how long since their previous message, and what changed across
    // the modules since then — so San can reason about "when" and continuity,
    // not just a static snapshot. lastSeenUtc is null on the first message.
    Task<string> BuildTimeContextAsync(DateTime? lastSeenUtc, CancellationToken ct = default);

    // NorthStar is San's brain / long-term memory. Recall pulls relevant stored
    // memories to ground a reply; Save writes a new durable memory back.
    Task<string?> RecallMemoriesAsync(string query, int limit = 8, CancellationToken ct = default);
    // Returns whether NorthStar actually accepted it. Still best-effort — a false
    // never throws — but the caller has to be able to tell, because advancing a
    // "distilled up to here" cursor past a memory that was never stored loses it
    // permanently and silently.
    // The origin says how far it can be trusted and where it came from (see MemoryOrigin);
    // without one it is saved as San's own, unconfirmed conclusion.
    Task<bool> SaveMemoryAsync(string content, string kind, int importance,
        MemoryOrigin? origin = null, CancellationToken ct = default);

    // What NorthStar already holds that relates to a piece of text (an email): entities
    // it names, matching memories, earlier events from the same senders or thread.
    // Null when NorthStar is unreachable -- the email is still read, just without it.
    Task<System.Text.Json.JsonElement?> GetRelatedAsync(
        string text, IReadOnlyList<string> senders, string? thread, CancellationToken ct = default);

    // An event in NorthStar's activity log; idempotent on eventKey. Returns its id
    // (also when it already existed), or null when NorthStar could not be reached.
    Task<Guid?> RecordEventAsync(string source, string kind, string title, string? detail,
        DateTime occurredAtUtc, string eventKey, CancellationToken ct = default);

    // Links a record to entities by name, creating any entity not yet known.
    Task LinkToEntitiesAsync(string recordType, Guid recordId, IEnumerable<string> entityNames,
        string source, string? sourceRef, CancellationToken ct = default);

    // NorthStar facts, formatted for the system prompt (stable between turns), and the
    // newest active insights, for the per-turn context. Null when there are none or
    // NorthStar is unreachable -- a chat turn never fails over either. See UserKnowledge.
    Task<string?> BuildUserFactsAsync(CancellationToken ct = default);
    Task<string?> BuildActiveInsightsAsync(int limit = 3, CancellationToken ct = default);

    // Records "this happened" into NorthStar's knowledge timeline (distinct from a
    // memory, which is a durable fact about the user). Email triage writes its
    // findings here so the brain — and therefore San's own time context on the next
    // turn — knows what came in without the user having to relay it.
    Task SaveKnowledgeAsync(string source, string topic, string summary, CancellationToken ct = default);

    // Everything the user has said they would do, from every store that holds one.
    Task<List<Commitment>> GetOpenCommitmentsAsync(CancellationToken ct = default);

    // Marks a NorthStar action item completed. Used when an email confirms the thing was
    // done, so the commitment stops being chased. Returns false rather than throwing when
    // NorthStar is unreachable: failing to close is a nuisance, failing the whole triage
    // run over it would lose the findings alongside it.
    Task<bool> CompleteActionAsync(string actionId, CancellationToken ct = default);

    // Today's habits and whether each is already ticked, for the agenda.
    Task<List<(string Name, bool DoneToday)>> GetTodaysHabitsAsync(CancellationToken ct = default);

    // NorthStar's week-by-week table of what actually happened. Raw JSON, passed to the
    // model verbatim and also used as the ground truth an insight is checked against —
    // so it must be the SAME string in both places, not two separate fetches.
    Task<string?> GetRollupAsync(CancellationToken ct = default);

    Task<bool> SaveInsightAsync(string title, string body, CancellationToken ct = default);

    // Trailing-30-day spend from Vault, used by the spending_threshold alert check. Returns
    // null if Vault is unreachable.
    Task<decimal?> GetTrailing30DaySpendAsync(CancellationToken ct = default);

    // The user's configured timezone (NorthStar "timezone" fact, falling back to the
    // container's own clock) — shared by time-context building and chat action scheduling
    // (e.g. converting "9am tomorrow" reminders to UTC) so both use the same source of truth.
    Task<TimeZoneInfo> ResolveTimeZoneAsync(CancellationToken ct = default);

    // Vitara's active health findings — what it has concluded, not what it measured.
    //
    // Deliberately typed rather than raw JSON. These go to the notification ledger,
    // which needs a key and a severity per finding; handing a worker a JSON blob to
    // pick apart is how the key ends up derived in three places and matching in none.
    Task<List<HealthFinding>> GetHealthFindingsAsync(CancellationToken ct = default);
}
