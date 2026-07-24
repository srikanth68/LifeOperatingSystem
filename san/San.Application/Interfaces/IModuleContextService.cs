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

    // Trailing-30-day spend from Vault, used by the spending_threshold alert check. Returns
    // null if Vault is unreachable.
    Task<decimal?> GetTrailing30DaySpendAsync(CancellationToken ct = default);

    // The user's configured timezone (NorthStar "timezone" fact, falling back to the
    // container's own clock) — shared by time-context building and chat action scheduling
    // (e.g. converting "9am tomorrow" reminders to UTC) so both use the same source of truth.
    Task<TimeZoneInfo> ResolveTimeZoneAsync(CancellationToken ct = default);
}
