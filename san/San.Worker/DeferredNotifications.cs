using System.Text.Json;
using Microsoft.Extensions.Logging;
using San.Application;
using San.Application.Interfaces;

namespace San.Worker;

// Findings held back by quiet hours, waiting for morning.
//
// A queue is necessary rather than just skipping the send, because the two workers
// differ in a way that matters. The system audit re-derives its findings from live
// state every run, so a skipped one comes back on its own. Email triage does NOT:
// its findings come from a batch of newly-fetched messages, and once LastCheckedAt
// moves past them those emails are never read again. Dropping a triage finding at
// 3 AM would lose it permanently.
//
// So the finding is recorded to NorthStar and the ledger immediately — nothing is
// forgotten — and only the Telegram push waits.
public static class DeferredNotifications
{
    private const string Key = "notify.deferred";

    private sealed class Held
    {
        public string Severity { get; set; } = "medium";
        public string Message { get; set; } = "";
        public string Source { get; set; } = "";
        public DateTime HeldAtUtc { get; set; }
    }

    public static async Task HoldAsync(ISanRepository repo, string severity, string message, string source)
    {
        var items = await LoadAsync(repo);
        // Same message twice while quiet is one message in the morning, not two. The
        // ledger's cooldown does not help here: it only counts messages that were
        // actually SENT, and these never were.
        if (items.Any(i => i.Message == message)) return;

        items.Add(new Held { Severity = severity, Message = message, Source = source, HeldAtUtc = DateTime.UtcNow });
        await SaveAsync(repo, items);
    }

    // Called at the start of every dispatch. Sends nothing while still quiet.
    public static async Task<int> FlushAsync(
        ISanRepository repo, ITelegramNotifier telegram, DateTime nowLocal, ILogger logger, CancellationToken ct)
    {
        if (QuietHours.IsQuiet(nowLocal)) return 0;

        var items = await LoadAsync(repo);
        if (items.Count == 0) return 0;

        var body = string.Join("\n", items
            .OrderByDescending(i => Rank(i.Severity))
            .Select(i => $"{Icon(i.Severity)} {i.Message}"));

        if (telegram.IsConfigured)
            await telegram.SendAsync($"🌅 Held overnight:\n{body}", ct);

        // Cleared only after the send: a failure here should leave them queued for the
        // next run rather than silently discarding them.
        await SaveAsync(repo, []);
        logger.LogInformation("Released {Count} notification(s) held through quiet hours.", items.Count);
        return items.Count;
    }

    private static async Task<List<Held>> LoadAsync(ISanRepository repo)
    {
        try
        {
            var raw = await repo.GetSettingAsync(Key);
            if (string.IsNullOrWhiteSpace(raw)) return [];
            return JsonSerializer.Deserialize<List<Held>>(raw) ?? [];
        }
        catch (JsonException)
        {
            // Corrupt queue costs at most one morning's summary; refusing to run would
            // cost every future one.
            return [];
        }
    }

    private static Task SaveAsync(ISanRepository repo, List<Held> items) =>
        repo.SetSettingAsync(Key, JsonSerializer.Serialize(items));

    private static int Rank(string severity) => severity switch
    {
        "critical" => 3, "high" => 2, "medium" => 1, _ => 0,
    };

    private static string Icon(string severity) => severity switch
    {
        "critical" => "🔴", "high" => "🟠", "low" => "⚪", _ => "🟡",
    };
}
