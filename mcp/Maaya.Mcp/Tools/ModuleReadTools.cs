using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Maaya.Time;

namespace Maaya.Mcp.Tools;

// One read tool per module, each with a `view` choosing what to read. See ModuleViews
// for why it is one tool with views rather than a tool per endpoint, and for the
// view -> endpoint mapping itself.
//
// These replaced ten single-endpoint tools (vault_finances, vitara_health,
// aasthi_properties, property_rent_status, property_unreceipted, sutra_documents,
// karma_habits, goals_list, nexus_market, nexus_alerts). Each module's old summary
// tool is the DEFAULT view of its replacement, so a call with no view answers what the
// old tool did, and the old trigger phrases stay in the descriptions.
//
// Things named by the user -- an account, a property, a habit -- are resolved to ids
// here, in code, never by asking the model to look an id up first. NameResolver says
// why: the model does not reliably chain that lookup.
[McpServerToolType]
public sealed class ModuleReadTools(ModuleGateway gw)
{
    [McpServerTool(Name = "vault_read")]
    [Description("Money, read-only. view: summary (default; net worth, cash, debt, 30-day spend by category - \"how much do I have\") · accounts (each account's balance) · transactions (from/to YYYY-MM-DD, query merchant/text, category, account name, limit 50 - \"what did I spend at X last month\") · recurring (subscriptions and bills found in the history) · monthly (spend per month; from = how far back) · sync (when bank data last refreshed). Not sure which module -> maaya_search.")]
    public async Task<string> VaultRead(
        string? view = null,
        string? from = null,
        string? to = null,
        string? query = null,
        string? category = null,
        string? account = null,
        int? limit = null)
    {
        var v = ModuleViews.Pick(view, "summary", ModuleViews.VaultViews);
        if (v is null) return ModuleViews.UnknownView("vault_read", view, ModuleViews.VaultViews);

        string? accountId = null;
        if (v == "transactions" && !string.IsNullOrWhiteSpace(account))
        {
            var (id, err) = await ResolveAsync(gw.GetAsync("vault", "/api/accounts"), account, "account", AccountNames);
            if (err is not null) return err;
            accountId = id;
        }

        var (path, error) = ModuleViews.Vault(v, from, to, query, category, accountId, limit, MaayaClock.Today);
        return error ?? await gw.GetAsync("vault", path!);
    }

    // An account is called by its bank as often as by its own name -- "the Chase
    // account" for one Plaid names "Total Checking" -- so both go into the name matched.
    public static string AccountNames(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var named = doc.RootElement.EnumerateArray()
                .Where(a => a.ValueKind == JsonValueKind.Object)
                .Select(a => new
                {
                    id = a.TryGetProperty("id", out var i) ? i.GetString() : null,
                    name = $"{Str(a, "institutionName")} {Str(a, "name")}".Trim(),
                });
            return JsonSerializer.Serialize(named);
        }
        catch (JsonException) { return json; }

        static string Str(JsonElement e, string p) =>
            e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }

    [McpServerTool(Name = "vitara_read")]
    [Description("Health records, read-only. view: today (default; readiness, sleep, activity, heart, recent workouts - \"how did I sleep\", \"am I recovered\") · sleep · readiness · activity (days 7; over 14 days returns averages) · workouts (days 30) · weight (days 90) · labs (recent blood tests) · measurements (days, metric) · vo2max · stress · spo2 · interventions (what the user is trying and since when). What the numbers MEAN against their own baseline -> health_findings.")]
    public async Task<string> VitaraRead(
        string? view = null,
        int? days = null,
        string? metric = null)
    {
        var v = ModuleViews.Pick(view, "today", ModuleViews.VitaraViews);
        if (v is null) return ModuleViews.UnknownView("vitara_read", view, ModuleViews.VitaraViews);

        var (path, error) = ModuleViews.Vitara(v, days, metric);
        return error ?? await gw.GetAsync("vitara", path!);
    }

    [McpServerTool(Name = "aasthi_read")]
    [Description("Rental properties, read-only. view: properties (default; each property, value, profit - \"my properties\") · property (one property in full, with its contacts) · tasks (status open|done) · rent (this month's rent and bills, expected vs received, what is late - \"did rent come in\") · financials · maintenance (one property, or the whole portfolio if none named) · documents (one property's files) · unreceipted (this year's spending with no receipt - \"tax paperwork\"; year). property = its address or part of it, e.g. \"Scoter\".")]
    public async Task<string> AasthiRead(
        string? view = null,
        string? property = null,
        string? status = null,
        int? year = null)
    {
        var v = ModuleViews.Pick(view, "properties", ModuleViews.AasthiViews);
        if (v is null) return ModuleViews.UnknownView("aasthi_read", view, ModuleViews.AasthiViews);

        string? propertyId = null;
        if (!string.IsNullOrWhiteSpace(property) && v != "properties" && v != "unreceipted")
        {
            var (id, err) = await ResolvePropertyAsync(gw, property);
            if (err is not null) return err;
            propertyId = id;
        }

        var (path, error) = ModuleViews.Aasthi(v, propertyId, status, year);
        return error ?? await gw.GetAsync("aasthi", path!);
    }

    [McpServerTool(Name = "karma_read")]
    [Description("Habits and goals, read-only. view: today (default; today's check-ins and active goals - \"did I do my habits\") · habits (every active habit) · history (one habit's check-ins; habit, days 30) · stats (streaks and completion rate; one habit, or all if none named - \"how are my streaks\") · goals (with progress; status active|completed - \"what are my goals\", \"how far along am I\").")]
    public async Task<string> KarmaRead(
        string? view = null,
        string? habit = null,
        int? days = null,
        string? status = null)
    {
        var v = ModuleViews.Pick(view, "today", ModuleViews.KarmaViews);
        if (v is null) return ModuleViews.UnknownView("karma_read", view, ModuleViews.KarmaViews);

        if (v == "today")
        {
            var today = await gw.GetAsync("karma", "/api/habits/today");
            var goals = await gw.GetAsync("karma", "/api/goals");
            return $"{{\"habitsToday\":{today},\"goals\":{goals}}}";
        }

        string? habitId = null;
        if (!string.IsNullOrWhiteSpace(habit) && v is "history" or "stats")
        {
            var (id, err) = await ResolveAsync(gw.GetAsync("karma", "/api/habits?active=true"), habit, "habit");
            if (err is not null) return err;
            habitId = id;
        }

        var (path, error) = ModuleViews.Karma(v, habitId, days, status);
        if (error is not null) return error;
        if (path is not null) return await gw.GetAsync("karma", path);

        // stats with no habit named: every active habit's, side by side.
        return await AllHabitStatsAsync(days);
    }

    private async Task<string> AllHabitStatsAsync(int? days)
    {
        var list = await gw.GetAsync("karma", "/api/habits?active=true");
        List<(string Id, string Name)> habits;
        try { habits = Habits(list); }
        catch (JsonException) { return list; }   // an error object from the gateway: pass it on

        if (habits.Count == 0) return "There are no active habits.";

        var d = Math.Clamp(days ?? 90, 1, 365);
        var stats = await Task.WhenAll(habits.Select(h =>
            gw.GetAsync("karma", $"/api/habits/{h.Id}/stats?days={d}")));

        var sb = new StringBuilder("[");
        for (var i = 0; i < habits.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"habit\":{JsonSerializer.Serialize(habits[i].Name)},\"stats\":{stats[i]}}}");
        }
        return sb.Append(']').ToString();
    }

    private static List<(string Id, string Name)> Habits(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("not a list");
        return doc.RootElement.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.Object)
            .Select(e => (Id: e.TryGetProperty("id", out var i) ? i.GetString() : null,
                          Name: e.TryGetProperty("name", out var n) ? n.GetString() : null))
            .Where(h => !string.IsNullOrWhiteSpace(h.Id))
            .Select(h => (h.Id!, h.Name ?? ""))
            .ToList();
    }

    [McpServerTool(Name = "sutra_read")]
    [Description("Document vault, read-only. view: stats (default without a query; counts by category, what is expiring) · search (default with a query; matching documents - query, category) · expiring (documents expiring within days 60 - \"is my passport expiring\").")]
    public Task<string> SutraRead(
        string? view = null,
        string? query = null,
        string? category = null,
        int? days = null)
    {
        var fallback = string.IsNullOrWhiteSpace(query) ? "stats" : "search";
        var v = ModuleViews.Pick(view, fallback, ModuleViews.SutraViews);
        if (v is null) return Task.FromResult(ModuleViews.UnknownView("sutra_read", view, ModuleViews.SutraViews));

        var (path, error) = ModuleViews.Sutra(v, query, category, days);
        return error is not null ? Task.FromResult(error) : gw.GetAsync("sutra", path!);
    }

    [McpServerTool(Name = "nexus_read")]
    [Description("Market monitor, read-only. view: status (default; tracked symbols, alert count, market open/closed) · alerts (alerts raised, newest first; days 7, limit 50) · board (every tracked symbol's latest read) · positions (holdings) · watchlist · premarket · ticker (one symbol in detail; symbol) · history (one symbol's past readings; symbol, limit 30).")]
    public Task<string> NexusRead(
        string? view = null,
        string? symbol = null,
        int? days = null,
        int? limit = null)
    {
        var v = ModuleViews.Pick(view, "status", ModuleViews.NexusViews);
        if (v is null) return Task.FromResult(ModuleViews.UnknownView("nexus_read", view, ModuleViews.NexusViews));

        var (path, error) = ModuleViews.Nexus(v, symbol, days, limit, DateTime.UtcNow);
        return error is not null ? Task.FromResult(error) : gw.GetAsync("nexus", path!);
    }

    // Properties have no name, only an address, and that is how they are spoken of:
    // "Scoter Street", "the Scoter house". Shared with the property write tools.
    internal static Task<(string? Id, string? Error)> ResolvePropertyAsync(ModuleGateway gw, string property) =>
        ResolveAsync(gw.GetAsync("aasthi", "/api/properties"), property, "property");

    // A module that is down answers the lookup with the gateway's error object, and
    // matching a name against that reads as "there are no properties" -- an outage
    // reported as an empty portfolio. The error goes back as it is instead.
    private static async Task<(string? Id, string? Error)> ResolveAsync(
        Task<string> list, string needle, string what, Func<string, string>? shape = null)
    {
        var json = await list;
        if (json.TrimStart().StartsWith("{\"error\"", StringComparison.Ordinal)) return (null, json);
        return NameResolver.Resolve(needle, shape is null ? json : shape(json), what);
    }
}
