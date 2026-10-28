using System.Globalization;
using System.Text.RegularExpressions;

namespace Maaya.Mcp.Tools;

// Which endpoint each module read tool's `view` lands on.
//
// The modules expose ~120 read endpoints and the catalogue used to reach about one in
// seven of them, mostly as a single summary per module: San could say what the net
// worth was but not what was spent last Tuesday, last night's sleep but not last
// week's. The obvious fix -- a tool per endpoint -- is the one thing that cannot be
// done: tool choice measurably degrades as the catalogue grows (95.7% correct at 11
// tools, 74.3% at 48, SAN-EVALS §4). So each module gets ONE read tool, and depth
// comes from a `view` argument instead of from more tools.
//
// Every view is a fixed path built here. The model picks a word from a list; it never
// composes a URL, so a read tool cannot be talked into reaching an endpoint nobody
// chose to expose, and nothing in this file can write.
//
// Pure functions, separate from the tool bodies, so the mapping is tested without a
// network. Name lookups (an account, a property, a habit) happen in the tools first,
// and the resolved id is passed in.
public static class ModuleViews
{
    public static readonly string[] VaultViews = ["summary", "accounts", "transactions", "recurring", "monthly", "sync"];
    public static readonly string[] VitaraViews =
        ["today", "sleep", "readiness", "activity", "workouts", "weight", "labs", "measurements", "vo2max", "stress", "spo2", "interventions"];
    public static readonly string[] AasthiViews =
        ["properties", "property", "tasks", "rent", "financials", "maintenance", "documents", "unreceipted"];
    public static readonly string[] KarmaViews = ["today", "habits", "history", "stats", "goals"];
    public static readonly string[] SutraViews = ["stats", "search", "expiring"];
    public static readonly string[] NexusViews = ["status", "alerts", "board", "positions", "watchlist", "premarket", "ticker", "history"];

    // The model writes "transaction", "Sleep", "vo2 max". Close enough is close enough:
    // a near miss resolves, and only a genuine miss comes back as an error naming the
    // choices, which the model can act on in the same turn.
    public static string? Pick(string? view, string fallback, string[] views)
    {
        if (string.IsNullOrWhiteSpace(view)) return fallback;
        var v = view.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
        foreach (var known in views)
        {
            var k = known.Replace("_", "");
            if (v == k || v + "s" == k || v == k + "s") return known;
        }
        return null;
    }

    public static string UnknownView(string tool, string? view, string[] views) =>
        $"{tool} has no view \"{view}\". Choose one of: {string.Join(", ", views)}.";

    // ── Vault ──

    public static (string? Path, string? Error) Vault(
        string view, string? from, string? to, string? query, string? category,
        string? accountId, int? limit, DateOnly today)
    {
        var (start, startErr) = Date(from, "from");
        if (startErr is not null) return (null, startErr);
        var (end, endErr) = Date(to, "to");
        if (endErr is not null) return (null, endErr);

        switch (view)
        {
            case "summary": return ("/api/summary", null);
            case "accounts": return ("/api/accounts", null);
            case "recurring": return ("/api/recurring", null);
            case "sync": return ("/api/sync/status/latest", null);
            case "monthly":
                // "Since March" is how people ask; the endpoint wants a month count.
                var months = start is { } s ? (today.Year - s.Year) * 12 + today.Month - s.Month + 1 : 12;
                return ($"/api/summary/monthly-spending?months={Math.Clamp(months, 1, 36)}", null);
            case "transactions":
                var q = new List<string> { $"limit={Math.Clamp(limit ?? 50, 1, 200)}" };
                if (start is { } a) q.Add($"startDate={a:yyyy-MM-dd}");
                if (end is { } b) q.Add($"endDate={b:yyyy-MM-dd}");
                if (!string.IsNullOrWhiteSpace(query)) q.Add($"q={Uri.EscapeDataString(query.Trim())}");
                if (!string.IsNullOrWhiteSpace(category)) q.Add($"category={Uri.EscapeDataString(category.Trim())}");
                if (!string.IsNullOrWhiteSpace(accountId)) q.Add($"accountId={Uri.EscapeDataString(accountId)}");
                return ($"/api/transactions?{string.Join("&", q)}", null);
            default: return (null, UnknownView("vault_read", view, VaultViews));
        }
    }

    // ── Vitara ──

    // Up to two weeks the nights are worth seeing one by one; past that, a row per
    // night is a wall of numbers that also overruns the size cap, and the question
    // being asked ("how did I sleep this month") is about the average anyway.
    public const int DailyRowsUpTo = 14;

    public static (string? Path, string? Error) Vitara(string view, int? days, string? metric)
    {
        int D(int fallback) => Math.Clamp(days ?? fallback, 1, 365);

        switch (view)
        {
            case "today": return ("/api/dashboard", null);
            case "sleep":
            case "readiness":
            case "activity":
                var d = D(7);
                return (d <= DailyRowsUpTo ? $"/api/{view}?days={d}" : $"/api/{view}/summary?days={d}", null);
            case "workouts": return ($"/api/workouts?days={D(30)}", null);
            case "weight": return ($"/api/weighins?days={D(90)}", null);
            case "labs": return ("/api/labs?limit=20", null);
            case "measurements":
                var m = string.IsNullOrWhiteSpace(metric) ? "" : $"&metric={Uri.EscapeDataString(metric.Trim())}";
                return ($"/api/measurements?days={D(90)}{m}", null);
            case "vo2max": return ($"/api/vo2max?days={D(180)}", null);
            case "stress": return ($"/api/stress?days={D(7)}", null);
            case "spo2": return ($"/api/spo2?days={D(7)}", null);
            case "interventions": return ("/api/interventions", null);
            default: return (null, UnknownView("vitara_read", view, VitaraViews));
        }
    }

    // ── Aasthi ──

    public static (string? Path, string? Error) Aasthi(string view, string? propertyId, string? status, int? year)
    {
        var p = propertyId is null ? "" : $"propertyId={propertyId}";

        switch (view)
        {
            case "properties": return ("/api/properties", null);
            case "property":
                return propertyId is null ? (null, NeedsProperty(view)) : ($"/api/properties/{propertyId}", null);
            case "documents":
                return propertyId is null ? (null, NeedsProperty(view)) : ($"/api/properties/{propertyId}/documents", null);
            case "tasks":
                var q = new List<string>();
                if (propertyId is not null) q.Add(p);
                if (!string.IsNullOrWhiteSpace(status)) q.Add($"status={Uri.EscapeDataString(status.Trim())}");
                return ("/api/tasks" + (q.Count > 0 ? "?" + string.Join("&", q) : ""), null);
            case "rent":
                return ("/api/recurring-charges/status" + (propertyId is null ? "" : "?" + p), null);
            // One property's entries, or the portfolio roll-up when none is named.
            case "financials":
                return (propertyId is null ? "/api/financials/summary" : $"/api/properties/{propertyId}/financials", null);
            case "maintenance":
                return (propertyId is null ? "/api/maintenance/summary" : $"/api/properties/{propertyId}/maintenance", null);
            case "unreceipted":
                return ("/api/ledger/unreceipted" + (year is > 0 ? $"?year={year}" : ""), null);
            default: return (null, UnknownView("aasthi_read", view, AasthiViews));
        }
    }

    private static string NeedsProperty(string view) =>
        $"aasthi_read view \"{view}\" needs a property: its address or part of it, e.g. \"Scoter\".";

    // ── Karma ──

    // stats without a habit is handled in the tool (one call per active habit), since
    // it is several requests rather than one path.
    public static (string? Path, string? Error) Karma(string view, string? habitId, int? days, string? status)
    {
        var d = Math.Clamp(days ?? 30, 1, 365);
        switch (view)
        {
            // The tool adds the active goals beside it, as the old karma_habits did.
            case "today": return ("/api/habits/today", null);
            case "habits": return ("/api/habits?active=true", null);
            case "goals":
                return ("/api/goals" + (string.IsNullOrWhiteSpace(status) ? "" : $"?status={Uri.EscapeDataString(status.Trim())}"), null);
            case "history":
                return habitId is null
                    ? (null, "karma_read view \"history\" needs a habit name, e.g. \"reading\".")
                    : ($"/api/habits/{habitId}/logs?days={d}", null);
            case "stats":
                return habitId is null ? (null, null) : ($"/api/habits/{habitId}/stats?days={Math.Clamp(days ?? 90, 1, 365)}", null);
            default: return (null, UnknownView("karma_read", view, KarmaViews));
        }
    }

    // ── Sutra ──

    public static (string? Path, string? Error) Sutra(string view, string? query, string? category, int? days)
    {
        switch (view)
        {
            case "stats": return ("/api/documents/stats", null);
            case "expiring": return ($"/api/documents/expiring?days={Math.Clamp(days ?? 60, 1, 730)}", null);
            case "search":
                var q = new List<string>();
                if (!string.IsNullOrWhiteSpace(query)) q.Add($"q={Uri.EscapeDataString(query.Trim())}");
                if (!string.IsNullOrWhiteSpace(category)) q.Add($"category={Uri.EscapeDataString(category.Trim())}");
                return ("/api/documents" + (q.Count > 0 ? "?" + string.Join("&", q) : ""), null);
            default: return (null, UnknownView("sutra_read", view, SutraViews));
        }
    }

    // ── Nexus ──

    // A ticker goes into the path itself, so it is held to what a ticker can look like
    // ("BRK.B", "^SPX", "NSE:TCS") rather than escaped and hoped about.
    private static readonly Regex Ticker = new(@"^[A-Z0-9.\-:^=]{1,20}$", RegexOptions.Compiled);

    public static (string? Path, string? Error) Nexus(string view, string? symbol, int? days, int? limit, DateTime utcNow)
    {
        const string root = "/api/nexus/sentinel";
        var sym = symbol?.Trim().ToUpperInvariant();

        switch (view)
        {
            case "status": return ($"{root}/status", null);
            case "board": return ($"{root}/board", null);
            case "positions": return ($"{root}/positions", null);
            case "watchlist": return ($"{root}/watchlist", null);
            case "premarket": return ($"{root}/premarket", null);
            case "alerts":
                var since = utcNow.AddDays(-Math.Clamp(days ?? 7, 1, 365)).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                return ($"{root}/alerts?since={Uri.EscapeDataString(since)}&limit={Math.Clamp(limit ?? 50, 1, 200)}", null);
            case "ticker":
            case "history":
                if (string.IsNullOrEmpty(sym)) return (null, $"nexus_read view \"{view}\" needs a symbol, e.g. \"AAPL\".");
                if (!Ticker.IsMatch(sym)) return (null, $"\"{symbol}\" does not look like a ticker symbol.");
                return view == "ticker"
                    ? ($"{root}/tickers/{Uri.EscapeDataString(sym)}", null)
                    : ($"{root}/tickers/{Uri.EscapeDataString(sym)}/history?limit={Math.Clamp(limit ?? 30, 1, 200)}", null);
            default: return (null, UnknownView("nexus_read", view, NexusViews));
        }
    }

    // YYYY-MM-DD, or nothing. A date the model garbled must fail loudly: silently
    // dropping it would turn "what did I spend in March" into "everything ever".
    private static (DateOnly? Date, string? Error) Date(string? s, string name)
    {
        if (string.IsNullOrWhiteSpace(s)) return (null, null);
        return DateOnly.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? (d, null)
            : (null, $"{name} must be a date as YYYY-MM-DD, got \"{s}\".");
    }
}
