using San.Application.DTOs;

namespace San.Application.Interfaces;

// Pulls live data from the other Maaya modules (Vault, Vitara, Aasthi) over HTTP.
// Used to (a) ground San's chat answers in real numbers, and (b) build the activity feed.
// Must degrade gracefully — any module being offline should not break chat or the feed.
public interface IModuleContextService
{
    Task<string> BuildChatContextAsync(CancellationToken ct = default);
    Task<FeedResult> GetActivityFeedAsync(CancellationToken ct = default);

    // Trailing-30-day spend from Vault, used by the spending_threshold alert check. Returns
    // null if Vault is unreachable.
    Task<decimal?> GetTrailing30DaySpendAsync(CancellationToken ct = default);
}
