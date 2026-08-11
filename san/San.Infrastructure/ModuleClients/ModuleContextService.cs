using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Maaya.Auth;
using San.Application;
using San.Application.DTOs;
using San.Application.Interfaces;

namespace San.Infrastructure.ModuleClients;

// Calls the other Maaya backends over plain HTTP using named clients ("vault", "vitara",
// "aasthi") registered in Program.cs with base addresses from VAULT_API_URL / VITARA_API_URL
// / AASTHI_API_URL. San intentionally does NOT reference those solutions' assemblies —
// staying HTTP-only keeps San deployable/runnable even if a sibling module's schema changes,
// as long as the JSON shape it reads here stays compatible.
//
// Every sibling module enforces Maaya's global JWT auth policy, so these calls MUST carry a
// Bearer token. San mints its own service token via the shared TokenService (same JWT_SECRET
// across all modules), which validates everywhere.
public class ModuleContextService(IHttpClientFactory httpFactory, TokenService tokens, IHealthTracker health) : IModuleContextService
{
    private string ServiceToken() => tokens.GenerateAccessToken("san-service", "san");
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

        // Sutra — document vault
        var sutra = await TryGetJsonAsync("sutra", "/api/documents/stats", ct);
        if (sutra is not null)
        {
            var docs = sutra.Value.TryGetProperty("totalCount", out var tc) ? tc.GetInt32() : 0;
            var expiring = sutra.Value.TryGetProperty("expiringSoon", out var ex) ? ex.GetInt32() : 0;
            var exNote = expiring > 0 ? $", {expiring} expiring soon" : "";
            lines.Add($"Sutra (documents): {docs} stored{exNote}.");
        }

        // Karma — habits & goals
        var habits = await TryGetJsonAsync("karma", "/api/habits/today", ct);
        var goals  = await TryGetJsonAsync("karma", "/api/goals", ct);
        if (habits is not null || goals is not null)
        {
            var bits = new List<string>();
            if (habits is { } h && h.ValueKind == JsonValueKind.Array)
            {
                var total = h.GetArrayLength();
                var done  = h.EnumerateArray().Count(x => x.TryGetProperty("todayCompleted", out var t) && t.ValueKind == JsonValueKind.True);
                if (total > 0) bits.Add($"{done}/{total} habits done today");
            }
            if (goals is { } g && g.ValueKind == JsonValueKind.Array)
            {
                var active = g.EnumerateArray().Count(x => x.TryGetProperty("status", out var s) && s.GetString() != "completed");
                if (active > 0) bits.Add($"{active} active goals");
            }
            if (bits.Count > 0) lines.Add($"Karma (goals & habits): {string.Join(", ", bits)}.");
        }

        // Nexus — trading engine
        var nexus = await TryGetJsonAsync("nexus", "/api/nexus/sentinel/status", ct);
        if (nexus is not null)
        {
            var tracked = nexus.Value.TryGetProperty("trackedCount", out var t) ? t.GetInt32() : 0;
            var alerts  = nexus.Value.TryGetProperty("openAlerts24h", out var a) ? a.GetInt32() : 0;
            var market  = nexus.Value.TryGetProperty("marketOpen", out var m) && m.ValueKind == JsonValueKind.True ? "open" : "closed";
            lines.Add($"Nexus (trading): {tracked} symbols tracked, {alerts} alerts (24h), US market {market}.");
        }

        // NorthStar — pending cross-module actions
        var north = await TryGetJsonAsync("northstar", "/api/context", ct);
        if (north is not null && north.Value.TryGetProperty("pendingActions", out var pa) && pa.ValueKind == JsonValueKind.Array && pa.GetArrayLength() > 0)
            lines.Add($"NorthStar (brain): {pa.GetArrayLength()} pending action(s) awaiting attention.");

        if (lines.Count == 0)
            return "No live module data is currently reachable (the module backends may not be running).";

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

        // Sutra: document stats + expiring-soon highlight
        var (sutra, sutraErr) = await TryGetJsonWithErrorAsync("sutra", "/api/documents/stats", ct);
        statuses.Add(new ModuleStatus("Sutra", sutra is not null, sutraErr));
        if (sutra is not null && sutra.Value.TryGetProperty("expiringSoon", out var exp) && exp.GetInt32() > 0)
            entries.Add(new ActivityFeedEntry("Sutra", "Documents expiring soon", $"{exp.GetInt32()}", DateTime.UtcNow));

        // Karma: habits completed today
        var (habits, karmaErr) = await TryGetJsonWithErrorAsync("karma", "/api/habits/today", ct);
        statuses.Add(new ModuleStatus("Karma", habits is not null, karmaErr));
        if (habits is not null && habits.Value.ValueKind == JsonValueKind.Array)
        {
            var total = habits.Value.GetArrayLength();
            var done  = habits.Value.EnumerateArray().Count(x => x.TryGetProperty("todayCompleted", out var t) && t.ValueKind == JsonValueKind.True);
            if (total > 0) entries.Add(new ActivityFeedEntry("Karma", "Habits today", $"{done}/{total}", DateTime.UtcNow));
        }

        // Nexus: trading engine status + open alerts
        var (nexus, nexusErr) = await TryGetJsonWithErrorAsync("nexus", "/api/nexus/sentinel/status", ct);
        statuses.Add(new ModuleStatus("Nexus", nexus is not null, nexusErr));
        if (nexus is not null && nexus.Value.TryGetProperty("openAlerts24h", out var al) && al.GetInt32() > 0)
            entries.Add(new ActivityFeedEntry("Nexus", "Open trade alerts (24h)", $"{al.GetInt32()}", DateTime.UtcNow));

        // NorthStar: pending cross-module actions
        var (north, northErr) = await TryGetJsonWithErrorAsync("northstar", "/api/context", ct);
        statuses.Add(new ModuleStatus("NorthStar", north is not null, northErr));
        if (north is not null && north.Value.TryGetProperty("pendingActions", out var pend) && pend.ValueKind == JsonValueKind.Array && pend.GetArrayLength() > 0)
            entries.Add(new ActivityFeedEntry("NorthStar", "Pending actions", $"{pend.GetArrayLength()}", DateTime.UtcNow));

        entries = entries.OrderByDescending(e => e.OccurredAt).Take(30).ToList();
        return new FeedResult(entries, statuses);
    }

    public async Task<string> BuildTimeContextAsync(DateTime? lastSeenUtc, CancellationToken ct = default)
    {
        var tzi = await ResolveTimeZoneAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tzi);
        var tzLabel = tzi.IsDaylightSavingTime(nowLocal) ? tzi.DaylightName : tzi.StandardName;

        var sb = new StringBuilder();
        sb.Append($"Right now it is {nowLocal:dddd, MMMM d, yyyy} at {nowLocal:h:mm tt} ({tzLabel}).");

        // The clock alone was never enough. "15:42" is a fact; "mid-afternoon on a
        // weekday, and they've been gone since yesterday" is a situation — and only a
        // situation can change how San answers. Derived here rather than left to the
        // model, which is reliable at acting on "late night" and unreliable at working
        // it out from a timestamp on every turn.
        sb.Append(' ');
        sb.Append(TimeAwareness.Describe(nowLocal, lastSeenUtc, nowUtc, tzi));

        if (lastSeenUtc is { } seen)
        {
            var localSeen = TimeZoneInfo.ConvertTimeFromUtc(seen, tzi);
            sb.Append($" Their previous message was {Humanize(nowUtc - seen)} ago (at {localSeen:h:mm tt}).");
        }

        if (QuietHours.IsQuiet(nowLocal))
            sb.Append($" (Notifications are currently held until {QuietHours.NextOpening(nowLocal):h:mm tt}.)");

        // What's happened across the modules since the last message — real, discrete,
        // event-level activity from NorthStar's append-only event log (each with its true
        // occurrence time), so San can reason in time-series, not just off a snapshot.
        var since = lastSeenUtc ?? nowUtc.AddHours(-24);
        var changes = await GetRecentActivityAsync(since, tzi, nowLocal.Date, ct);
        if (changes.Count > 0)
            sb.Append("\nWhat's happened across the modules since then:\n" + string.Join("\n", changes.Select(c => "- " + c)));

        return sb.ToString();
    }

    // Timezone resolution: prefer the user's explicit setting (NorthStar "timezone"
    // fact, set in Settings), fall back to the container's own clock (TZ env, set
    // in the Dockerfile). Keeps San aligned with the rest of Maaya.
    public async Task<TimeZoneInfo> ResolveTimeZoneAsync(CancellationToken ct = default)
    {
        var fact = await TryGetJsonAsync("northstar", "/api/facts/timezone", ct);
        if (fact is { } f && f.TryGetProperty("value", out var v) && v.GetString() is { Length: > 0 } tzId)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(tzId); }
            catch { /* unknown id — fall through to local */ }
        }
        return TimeZoneInfo.Local;
    }

    // Discrete events that occurred since the user's last message, newest first, each
    // stamped with its real local occurrence time so San can reason about ordering and
    // timing. Sourced from NorthStar's append-only event log (/api/events?since=<ts>),
    // which carries per-transaction / per-check-in / per-reminder rows with true dates —
    // NOT the 15-minute state snapshots the knowledge timeline holds. If NorthStar is
    // unreachable this returns empty and time-context degrades to just the clock; chat
    // is never blocked.
    private async Task<List<string>> GetRecentActivityAsync(DateTime sinceUtc, TimeZoneInfo tzi, DateTime nowLocalDate, CancellationToken ct)
    {
        var sinceIso = Uri.EscapeDataString(sinceUtc.ToUniversalTime().ToString("o"));
        var result = await TryGetJsonAsync("northstar", $"/api/events?since={sinceIso}&limit=40", ct);
        if (result is not { } r || !r.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            return [];

        var items = new List<string>();
        foreach (var e in events.EnumerateArray())
        {
            var title = e.TryGetProperty("title", out var ti) ? ti.GetString() : null;
            if (string.IsNullOrWhiteSpace(title)) continue;

            var src = e.TryGetProperty("source", out var s) ? s.GetString() : null;
            var detail = e.TryGetProperty("detail", out var dt) && dt.ValueKind == JsonValueKind.String ? dt.GetString() : null;

            string? whenLabel = null;
            if (e.TryGetProperty("occurredAt", out var oa) && oa.ValueKind == JsonValueKind.String
                && DateTime.TryParse(oa.GetString(), null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var occurred))
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(occurred, tzi);
                whenLabel = local.Date == nowLocalDate ? local.ToString("h:mm tt") : local.ToString("MMM d, h:mm tt");
            }

            var line = string.IsNullOrWhiteSpace(src) ? title! : $"{src}: {title}";
            if (!string.IsNullOrWhiteSpace(detail)) line += $" ({detail})";
            if (whenLabel is not null) line += $" — {whenLabel}";
            items.Add(line);
        }
        return items;
    }

    private static string Humanize(TimeSpan g)
    {
        if (g.TotalMinutes < 1) return "moments";
        if (g.TotalMinutes < 60) return $"{(int)g.TotalMinutes} minute{Plural((int)g.TotalMinutes)}";
        if (g.TotalHours < 24) return $"{(int)g.TotalHours} hour{Plural((int)g.TotalHours)}";
        return $"{(int)g.TotalDays} day{Plural((int)g.TotalDays)}";
    }

    private static string Plural(int n) => n == 1 ? "" : "s";

    public async Task<decimal?> GetTrailing30DaySpendAsync(CancellationToken ct = default)
    {
        var vault = await TryGetJsonAsync("vault", "/api/summary", ct);
        if (vault is null || !vault.Value.TryGetProperty("spendingByCategory", out var cats)) return null;
        decimal total = 0;
        foreach (var c in cats.EnumerateArray())
            total += (decimal)c.GetProperty("totalAmount").GetDouble();
        return total;
    }

    // ── NorthStar brain: recall + save ──

    public async Task<string?> RecallMemoriesAsync(string query, int limit = 8, CancellationToken ct = default)
    {
        var escaped = Uri.EscapeDataString(query);
        var result = await TryGetJsonAsync("northstar", $"/api/memory/recall?q={escaped}&limit={limit}", ct);
        if (result is not { } r || !r.TryGetProperty("memories", out var mems) || mems.ValueKind != JsonValueKind.Array || mems.GetArrayLength() == 0)
            return null;

        var lines = new List<string>();
        foreach (var m in mems.EnumerateArray())
        {
            var content = m.TryGetProperty("content", out var c) ? c.GetString() : null;
            var kind = m.TryGetProperty("kind", out var k) ? k.GetString() : null;
            if (!string.IsNullOrWhiteSpace(content))
                lines.Add(string.IsNullOrWhiteSpace(kind) ? $"- {content}" : $"- ({kind}) {content}");
        }
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    public Task<bool> SaveMemoryAsync(string content, string kind, int importance, CancellationToken ct = default) =>
        PostToBrainAsync(
            "/api/memory",
            new { content, kind, importance, source = "san" },
            HealthComponents.NorthStarWrite, ct);

    public async Task SaveKnowledgeAsync(string source, string topic, string summary, CancellationToken ct = default) =>
        await PostToBrainAsync(
            "/api/ingest",
            new { source, topic, summary, day = DateTime.UtcNow.ToString("yyyy-MM-dd") },
            HealthComponents.NorthStarWrite, ct);

    // Writes to NorthStar stay best-effort — losing one must never take down the chat
    // turn or worker run that produced it — but they are no longer SILENT. Both of
    // these used to swallow the exception and, worse, ignore the response status
    // entirely: a 401 from an expired service token counted as a successful save. San
    // would go on believing it had a brain while everything it learned went nowhere.
    public async Task<string?> GetRollupAsync(CancellationToken ct = default)
    {
        try
        {
            var http = httpFactory.CreateClient("northstar");
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/rollup?weeks=8");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ServiceToken());
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                await health.RecordAsync(HealthComponents.NorthStarRecall, false,
                    $"HTTP {(int)resp.StatusCode} from /api/rollup", ct);
                return null;
            }
            await health.RecordAsync(HealthComponents.NorthStarRecall, true, ct: ct);
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            await health.RecordAsync(HealthComponents.NorthStarRecall, false, ex.Message, ct);
            return null;
        }
    }

    public Task<bool> SaveInsightAsync(string title, string body, CancellationToken ct = default) =>
        PostToBrainAsync(
            "/api/insights",
            // Tagged with the model, not just "san": an insight is a claim, and knowing
            // which model made it is the difference between auditing a bad one and
            // guessing at it.
            new { title, body, generatedBy = $"san-insights/{Environment.GetEnvironmentVariable("LLM_MODEL") ?? "llm"}" },
            HealthComponents.NorthStarWrite, ct);

    private async Task<bool> PostToBrainAsync(string path, object payload, string component, CancellationToken ct)
    {
        try
        {
            var http = httpFactory.CreateClient("northstar");
            using var req = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ServiceToken());
            using var resp = await http.SendAsync(req, ct);

            if (resp.IsSuccessStatusCode)
            {
                await health.RecordAsync(component, true, ct: ct);
                return true;
            }

            await health.RecordAsync(component, false, $"HTTP {(int)resp.StatusCode} from {path}", ct);
            return false;
        }
        catch (Exception ex)
        {
            await health.RecordAsync(component, false, ex.Message, ct);
            return false;
        }
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
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ServiceToken());
            using var resp = await http.SendAsync(req, ct);
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
