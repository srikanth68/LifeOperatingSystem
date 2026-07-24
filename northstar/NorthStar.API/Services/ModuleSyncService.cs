using System.Net.Http.Headers;
using System.Text.Json;
using Maaya.Auth;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.API.Services;

// Single source of truth for "pull every module's summary, snapshot it, and
// distill knowledge entries." Called by ContextController's manual sync button
// AND by NorthStarSyncWorker's 15-minute timer — never duplicate this logic.
public class ModuleSyncService(INorthStarRepository repo, IHttpClientFactory httpFactory, TokenService tokens)
{
    private static readonly string[] Modules = ["vault", "vitara", "aasthi", "san", "sutra"];

    public async Task<Dictionary<string, object?>> SyncAllAsync()
    {
        // Env-overridable so this works in containers (service DNS) and across machines.
        var baseUrls = new Dictionary<string, string>
        {
            ["vault"] = Environment.GetEnvironmentVariable("VAULT_API_URL") ?? "http://localhost:5000",
            ["vitara"] = Environment.GetEnvironmentVariable("VITARA_API_URL") ?? "http://localhost:5100",
            ["aasthi"] = Environment.GetEnvironmentVariable("AASTHI_API_URL") ?? "http://localhost:5200",
            ["san"] = Environment.GetEnvironmentVariable("SAN_API_URL") ?? "http://localhost:5300",
            ["sutra"] = Environment.GetEnvironmentVariable("SUTRA_API_URL") ?? "http://localhost:5400",
        };
        var endpoints = new Dictionary<string, string>
        {
            ["vault"] = "/api/summary",
            ["vitara"] = "/api/dashboard",
            ["aasthi"] = "/api/properties",
            ["san"] = "/api/people?limit=5",
            ["sutra"] = "/api/documents/stats",
        };

        var results = new Dictionary<string, object?>();
        var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        // Sibling modules enforce Maaya JWT auth — mint a service token (shared secret).
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.GenerateAccessToken("northstar-service", "northstar"));

        foreach (var mod in Modules)
        {
            var url = $"{baseUrls[mod]}{endpoints[mod]}";
            string? error = null;
            string? json = null;

            try
            {
                var resp = await client.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                    json = await resp.Content.ReadAsStringAsync();
                else
                    error = $"HTTP {(int)resp.StatusCode}";
            }
            catch (Exception ex)
            {
                error = ex.Message.Length > 100 ? ex.Message[..100] : ex.Message;
            }

            await repo.UpsertModuleSyncAsync(new() { Module = mod, LastSyncAt = DateTime.UtcNow, LastError = error });

            if (json is not null)
            {
                await repo.UpsertSnapshotAsync(new() { Module = mod, SummaryJson = json });
                results[mod] = "synced";

                // Best-effort: turn the raw snapshot into a human-readable knowledge
                // entry (one per module per day, upserted). A parse miss here must
                // never fail the sync — the snapshot itself already succeeded above.
                try
                {
                    var distilled = DistillKnowledge(mod, json);
                    if (distilled is not null)
                        await repo.UpsertDailyEntryAsync(mod, distilled.Value.Topic, distilled.Value.Summary, json,
                            DateOnly.FromDateTime(DateTime.UtcNow));
                }
                catch { /* unexpected shape from a module — skip, snapshot still saved */ }

                // Bootstrap the event-level log from the real-dated data we just pulled.
                // Idempotent on EventKey, so re-harvesting the same transaction/property
                // every 15 min inserts each exactly once. This gives San a true event
                // history even before individual modules are wired to POST /api/events
                // directly. A parse miss must never fail the sync.
                try
                {
                    var harvested = HarvestEvents(mod, json);
                    if (harvested.Count > 0)
                        await repo.AddEventsIfNewAsync(harvested);
                }
                catch { /* unexpected shape — skip, snapshot + knowledge still saved */ }
            }
            else
            {
                results[mod] = error;
            }
        }

        return results;
    }

    // Derive discrete, real-dated events from a module's snapshot payload. Only modules
    // whose summary already carries individual dated records are harvested here (Vault
    // transactions, Aasthi property adds). Everything else is expected to POST its own
    // events to /api/events as they happen (habit check-ins, reminders fired, uploads),
    // since those have no real occurrence date in a rolled-up summary.
    private static List<ActivityEvent> HarvestEvents(string module, string json)
    {
        var events = new List<ActivityEvent>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        switch (module)
        {
            case "vault" when root.ValueKind == JsonValueKind.Object
                              && root.TryGetProperty("recentTransactions", out var txns)
                              && txns.ValueKind == JsonValueKind.Array:
                foreach (var t in txns.EnumerateArray())
                {
                    var id = t.TryGetProperty("id", out var tid) ? tid.GetString() : null;
                    if (string.IsNullOrEmpty(id)) continue; // no stable key → can't dedup, skip
                    if (!t.TryGetProperty("transactionDate", out var td) || td.ValueKind != JsonValueKind.String
                        || !DateTime.TryParse(td.GetString(), null,
                            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                            out var when)) continue;

                    var amount = t.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDecimal() : 0;
                    var merchant = t.TryGetProperty("merchantName", out var m) && m.ValueKind == JsonValueKind.String && m.GetString() is { Length: > 0 } mn
                        ? mn
                        : (t.TryGetProperty("description", out var d) ? d.GetString() : null) ?? "Transaction";
                    var category = t.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;

                    events.Add(new ActivityEvent
                    {
                        Source = "vault",
                        Kind = "transaction",
                        Title = $"{merchant} — ${amount:N2}",
                        Detail = category,
                        OccurredAt = when,
                        EventKey = $"vault:transaction:{id}",
                    });
                }
                break;

            case "aasthi" when root.ValueKind == JsonValueKind.Array:
                foreach (var p in root.EnumerateArray())
                {
                    var id = p.TryGetProperty("id", out var pid) ? pid.GetString() : null;
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!p.TryGetProperty("createdAt", out var ca) || ca.ValueKind != JsonValueKind.String
                        || !DateTime.TryParse(ca.GetString(), null,
                            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                            out var when)) continue;
                    var address = p.TryGetProperty("address", out var ad) ? ad.GetString() : null;
                    events.Add(new ActivityEvent
                    {
                        Source = "aasthi",
                        Kind = "property_added",
                        Title = $"Property added: {address}",
                        OccurredAt = when,
                        EventKey = $"aasthi:property_added:{id}",
                    });
                }
                break;
        }

        return events;
    }

    private static (string Topic, string Summary)? DistillKnowledge(string module, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        switch (module)
        {
            case "vault" when root.ValueKind == JsonValueKind.Object:
                if (!root.TryGetProperty("netWorth", out var nw)) return null;
                var cash = root.TryGetProperty("totalCash", out var c) ? c.GetDecimal() : 0;
                var debt = root.TryGetProperty("totalDebt", out var d) ? d.GetDecimal() : 0;
                return ("finance", $"Net worth ${nw.GetDecimal():N0} — cash ${cash:N0}, debt ${debt:N0}.");

            case "vitara" when root.ValueKind == JsonValueKind.Object:
                string? Score(string section) =>
                    root.TryGetProperty(section, out var s) && s.ValueKind == JsonValueKind.Object
                        && s.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number
                        ? sc.GetInt32().ToString() : null;
                var sleepScore = Score("sleep");
                var readyScore = Score("readiness");
                var actScore = Score("activity");
                if (sleepScore is null && readyScore is null && actScore is null) return null;
                var steps = root.TryGetProperty("activity", out var act) && act.ValueKind == JsonValueKind.Object
                    && act.TryGetProperty("steps", out var st) && st.ValueKind == JsonValueKind.Number
                    ? st.GetInt32() : (int?)null;
                var parts = new List<string>();
                if (sleepScore is not null) parts.Add($"sleep {sleepScore}");
                if (readyScore is not null) parts.Add($"readiness {readyScore}");
                if (actScore is not null) parts.Add($"activity {actScore}");
                var summary = char.ToUpper(parts[0][0]) + parts[0][1..] + string.Concat(parts.Skip(1).Select(p => $", {p}"));
                if (steps.HasValue) summary += $", {steps:N0} steps";
                return ("health", summary + ".");

            case "aasthi" when root.ValueKind == JsonValueKind.Array:
                if (root.GetArrayLength() == 0) return null;
                decimal totalValue = 0, totalProfit = 0;
                foreach (var p in root.EnumerateArray())
                {
                    if (p.TryGetProperty("currentValue", out var cv)) totalValue += cv.GetDecimal();
                    if (p.TryGetProperty("profitAmount", out var pa)) totalProfit += pa.GetDecimal();
                }
                var n = root.GetArrayLength();
                return ("property", $"{n} propert{(n == 1 ? "y" : "ies")}, combined value ${totalValue:N0}, profit ${totalProfit:N0}.");

            case "sutra" when root.ValueKind == JsonValueKind.Object:
                if (!root.TryGetProperty("totalCount", out var tc) || tc.GetInt32() == 0) return null;
                var size = root.TryGetProperty("totalSize", out var ts) ? ts.GetString() : "";
                var expiring = root.TryGetProperty("expiringSoon", out var es) ? es.GetInt32() : 0;
                var doc2 = $"{tc.GetInt32()} documents on file ({size})";
                if (expiring > 0) doc2 += $", {expiring} expiring soon";
                return ("documents", doc2 + ".");

            default:
                return null; // san: not enough signal in a capped people list to distill
        }
    }
}
