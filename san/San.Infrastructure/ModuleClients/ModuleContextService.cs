using System.Net.Http;
using System.Text.Json;
using San.Application.DTOs;
using San.Application.Interfaces;

namespace San.Infrastructure.ModuleClients;

// Calls the other Maaya backends over plain HTTP using named clients ("vault", "vitara",
// "aasthi") registered in Program.cs with base addresses from VAULT_API_URL / VITARA_API_URL
// / AASTHI_API_URL. San intentionally does NOT reference those solutions' assemblies —
// staying HTTP-only keeps San deployable/runnable even if a sibling module's schema changes,
// as long as the JSON shape it reads here stays compatible.
public class ModuleContextService(IHttpClientFactory httpFactory) : IModuleContextService
{
    public async Task<string> BuildChatContextAsync(CancellationToken ct = default)
    {
        var lines = new List<string>();

        var vault = await TryGetJsonAsync("vault", "/api/summary", ct);
        if (vault is not null)
        {
            var netWorth = vault.Value.GetProperty("netWorth").GetDecimal();
            var cash     = vault.Value.GetProperty("totalCash").GetDecimal();
            var debt     = vault.Value.GetProperty("totalDebt").GetDecimal();
            lines.Add($"Vault (finances): net worth ${netWorth:N0}, cash ${cash:N0}, debt ${debt:N0}.");

            if (vault.Value.TryGetProperty("spendingByCategory", out var cats) && cats.GetArrayLength() > 0)
            {
                var top = cats.EnumerateArray().Take(3)
                    .Select(c => $"{c.GetProperty("category").GetString()} ${c.GetProperty("totalAmount").GetDouble():N0}");
                lines.Add($"Top spending categories (30d): {string.Join(", ", top)}.");
            }
        }

        var readiness = await TryGetJsonAsync("vitara", "/api/readiness/summary", ct);
        var sleep     = await TryGetJsonAsync("vitara", "/api/sleep/summary", ct);
        var activity  = await TryGetJsonAsync("vitara", "/api/activity/summary", ct);
        if (readiness is not null || sleep is not null || activity is not null)
        {
            var bits = new List<string>();
            if (readiness is { } r && r.TryGetProperty("avgScore", out var rs)) bits.Add($"readiness avg {rs.GetDouble():N0}");
            if (sleep is { } s && s.TryGetProperty("avgScore", out var ss)) bits.Add($"sleep avg {ss.GetDouble():N0}");
            if (activity is { } a && a.TryGetProperty("avgScore", out var asc)) bits.Add($"activity avg {asc.GetDouble():N0}");
            if (bits.Count > 0) lines.Add($"Vitara (health, last 30d): {string.Join(", ", bits)}.");
        }

        var aasthi = await TryGetJsonAsync("aasthi", "/api/properties/summary", ct);
        if (aasthi is not null)
        {
            var count  = aasthi.Value.GetProperty("propertyCount").GetInt32();
            var profit = aasthi.Value.GetProperty("totalProfit").GetDecimal();
            lines.Add($"Aasthi (real estate): {count} properties, total profit ${profit:N0}.");
        }

        if (lines.Count == 0)
            return "No live module data is currently reachable (Vault/Vitara/Aasthi backends may not be running).";

        return "Current snapshot across the user's Maaya modules:\n" + string.Join("\n", lines);
    }

    public async Task<FeedResult> GetActivityFeedAsync(CancellationToken ct = default)
    {
        var entries = new List<ActivityFeedEntry>();
        var statuses = new List<ModuleStatus>();

        // Vault: recent transactions
        var (vault, vaultErr) = await TryGetJsonWithErrorAsync("vault", "/api/summary", ct);
        statuses.Add(new ModuleStatus("Vault", vault is not null, vaultErr));
        if (vault is not null && vault.Value.TryGetProperty("recentTransactions", out var txns))
        {
            foreach (var t in txns.EnumerateArray())
            {
                var amount = t.GetProperty("amount").GetDecimal();
                var merchant = t.TryGetProperty("merchantName", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString() : t.GetProperty("description").GetString();
                var when = t.GetProperty("transactionDate").GetDateTime();
                entries.Add(new ActivityFeedEntry("Vault", merchant ?? "Transaction", $"${amount:N2}", when));
            }
        }

        // Vitara: rolled-up 30-day summaries (snapshots, not discrete events)
        var (readiness, vitaraErr) = await TryGetJsonWithErrorAsync("vitara", "/api/readiness/summary", ct);
        statuses.Add(new ModuleStatus("Vitara", readiness is not null, vitaraErr));
        if (readiness is not null && readiness.Value.TryGetProperty("avgScore", out var avgR))
            entries.Add(new ActivityFeedEntry("Vitara", "Readiness (30d avg)", $"{avgR.GetDouble():N0}", DateTime.UtcNow));
        var sleep = await TryGetJsonAsync("vitara", "/api/sleep/summary", ct);
        if (sleep is not null && sleep.Value.TryGetProperty("avgScore", out var avgS))
            entries.Add(new ActivityFeedEntry("Vitara", "Sleep (30d avg)", $"{avgS.GetDouble():N0}", DateTime.UtcNow));

        // Aasthi: recently added properties
        var (aasthi, aasthiErr) = await TryGetJsonWithErrorAsync("aasthi", "/api/properties", ct);
        statuses.Add(new ModuleStatus("Aasthi", aasthi is not null, aasthiErr));
        if (aasthi is not null)
        {
            foreach (var p in aasthi.Value.EnumerateArray())
            {
                var when = p.GetProperty("createdAt").GetDateTime();
                entries.Add(new ActivityFeedEntry("Aasthi", "Property added", p.GetProperty("address").GetString() ?? "", when));
            }
        }

        entries = entries.OrderByDescending(e => e.OccurredAt).Take(30).ToList();
        return new FeedResult(entries, statuses);
    }

    public async Task<decimal?> GetTrailing30DaySpendAsync(CancellationToken ct = default)
    {
        var vault = await TryGetJsonAsync("vault", "/api/summary", ct);
        if (vault is null || !vault.Value.TryGetProperty("spendingByCategory", out var cats)) return null;
        decimal total = 0;
        foreach (var c in cats.EnumerateArray())
            total += (decimal)c.GetProperty("totalAmount").GetDouble();
        return total;
    }

    private async Task<JsonElement?> TryGetJsonAsync(string client, string path, CancellationToken ct)
    {
        var (json, _) = await TryGetJsonWithErrorAsync(client, path, ct);
        return json;
    }

    private async Task<(JsonElement? json, string? error)> TryGetJsonWithErrorAsync(string client, string path, CancellationToken ct)
    {
        try
        {
            var http = httpFactory.CreateClient(client);
            using var resp = await http.GetAsync(path, ct);
            if (!resp.IsSuccessStatusCode) return (null, $"HTTP {(int)resp.StatusCode}");
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return (doc.RootElement.Clone(), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
