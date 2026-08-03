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
    Task SaveMemoryAsync(string content, string kind, int importance, CancellationToken ct = default);

    // Records "this happened" into NorthStar's knowledge timeline (distinct from a
    // memory, which is a durable fact about the user). Email triage writes its
    // findings here so the brain — and therefore San's own time context on the next
    // turn — knows what came in without the user having to relay it.
    Task SaveKnowledgeAsync(string source, string topic, string summary, CancellationToken ct = default);

    // Trailing-30-day spend from Vault, used by the spending_threshold alert check. Returns
    // null if Vault is unreachable.
    Task<decimal?> GetTrailing30DaySpendAsync(CancellationToken ct = default);

    // The user's configured timezone (NorthStar "timezone" fact, falling back to the
    // container's own clock) — shared by time-context building and chat action scheduling
    // (e.g. converting "9am tomorrow" reminders to UTC) so both use the same source of truth.
    Task<TimeZoneInfo> ResolveTimeZoneAsync(CancellationToken ct = default);
}
