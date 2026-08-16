using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Maaya.Mcp.Tools;

// Read tools over the live Maaya modules. All calls are JWT-authenticated
// server-side; responses are size-capped JSON straight from each module's API.
[McpServerToolType]
public sealed class ModuleTools(ModuleGateway gw)
{
    // Cheap authed endpoint per module, used by maaya_status.
    private static readonly Dictionary<string, string> Probes = new()
    {
        ["vault"] = "/api/summary",
        ["vitara"] = "/api/dashboard",
        ["aasthi"] = "/api/properties",
        ["san"] = "/api/people?limit=1",
        ["sutra"] = "/api/documents/stats",
        ["karma"] = "/api/habits/today",
        ["nexus"] = "/api/nexus/sentinel/status",
        ["northstar"] = "/api/memory/stats",
    };

    // Where a free-text search lands in each module. Kept here rather than spread
    // through the tool body so adding a searchable module is one line.
    private static readonly (string Module, string Label, string PathFormat)[] SearchTargets =
    [
        ("sutra", "documents", "/api/documents?q={0}"),
        ("aasthi", "property records", "/api/search?q={0}&limit=10"),
        ("vault", "transactions", "/api/transactions?q={0}&limit=25"),
        ("northstar", "long-term memory", "/api/knowledge/search?q={0}"),
    ];

    [McpServerTool(Name = "agenda_now")]
    [Description("Ranked NOW list merging calendar, reminders, active alerts, NorthStar items, property tasks, today's unticked habits. Overdue/in-progress first. For \"what's on\", \"what should I be doing\", \"what am I forgetting\", \"where do I need to be\". Prefer over querying modules separately. limit (12)")]
    public Task<string> AgendaNow(
        int? limit = null) =>
        gw.GetAsync("san", $"/api/agenda?limit={Math.Clamp(limit ?? 12, 1, 50)}");

    [McpServerTool(Name = "maaya_search")]
    [Description("Search the whole system at once: Sutra docs, Aasthi property/maintenance records, Vault transactions, NorthStar memory. Use whenever the user asks where something is or to find something without naming a module - \"find the HVAC invoice\", \"what did I spend at Home Depot\", \"when did I last service the boiler\". Results grouped by source; empty groups omitted. query*")]
    public async Task<string> MaayaSearch(
        string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Provide something to search for.";

        var term = Uri.EscapeDataString(query.Trim());

        // Fanned out in parallel: searching five modules one after another would put
        // five sequential HTTP round-trips inside a single tool call, which the user
        // waits through.
        var results = await Task.WhenAll(SearchTargets.Select(async t =>
        {
            var body = await gw.GetAsync(t.Module, string.Format(t.PathFormat, term));
            return (t.Label, Body: body);
        }));

        var sb = new System.Text.StringBuilder();
        var found = 0;
        foreach (var (label, body) in results)
        {
            // An empty array, an empty object, or an error from one module must not
            // hide the modules that did answer — report per-source and keep going.
            if (LooksEmpty(body)) continue;
            found++;
            sb.AppendLine($"## {label}");
            sb.AppendLine(body);
            sb.AppendLine();
        }

        return found == 0
            ? $"No matches for \"{query}\" in documents, property records, transactions, or long-term memory."
            : sb.ToString().TrimEnd();
    }

    private static bool LooksEmpty(string body)
    {
        var t = body.Trim();
        if (t.Length == 0 || t is "[]" or "{}" or "null") return true;
        // The gateway turns an unreachable module into an error object; that is worth
        // knowing about but is not a search hit, and listing it as one would have San
        // report an outage as though it were a result.
        if (t.StartsWith("{\"error\"", StringComparison.Ordinal)) return true;
        // Aasthi always answers with its four groups, so emptiness is total:0.
        return t.Contains("\"total\":0", StringComparison.Ordinal);
    }

    [McpServerTool(Name = "vault_finances")]
    [Description("Net worth, cash, debt, 30-day spending by category. For \"how much do I have\", \"what's my net worth\", \"what am I spending on\". Specific merchant/transaction -> maaya_search.")]
    public Task<string> VaultFinances() => gw.GetAsync("vault", "/api/summary");

    [McpServerTool(Name = "vitara_health")]
    [Description("Readiness, sleep, activity, heart metrics, recent workouts. For \"how did I sleep\", \"what's my readiness\", \"am I recovered\".")]
    public Task<string> VitaraHealth() => gw.GetAsync("vitara", "/api/dashboard");

    [McpServerTool(Name = "aasthi_properties")]
    [Description("Real-estate portfolio: properties, values, profit. For \"my properties\", \"how are rentals doing\". Specific repair/cost/vendor -> maaya_search.")]
    public Task<string> AasthiProperties() => gw.GetAsync("aasthi", "/api/properties");

    [McpServerTool(Name = "sutra_documents")]
    [Description("Document vault. No query -> stats (counts by category, expiring soon). With query -> matching docs. query")]
    public Task<string> SutraDocuments(
        string? query = null) =>
        string.IsNullOrWhiteSpace(query)
            ? gw.GetAsync("sutra", "/api/documents/stats")
            : gw.GetAsync("sutra", $"/api/documents?q={Uri.EscapeDataString(query)}");

    [McpServerTool(Name = "karma_habits")]
    [Description("Today's check-ins + active goals, with habit ids for habit_checkin. For \"did I do my habits\", \"how are my streaks\".")]
    public async Task<string> KarmaHabits()
    {
        var habits = await gw.GetAsync("karma", "/api/habits/today");
        var goals = await gw.GetAsync("karma", "/api/goals");
        return $"{{\"habitsToday\":{habits},\"goals\":{goals}}}";
    }

    [McpServerTool(Name = "nexus_market")]
    [Description("Monitor status: tracked symbols, alert COUNT, market open/closed. Actual alerts -> nexus_alerts.")]
    public Task<string> NexusMarket() => gw.GetAsync("nexus", "/api/nexus/sentinel/status");

    [McpServerTool(Name = "nexus_alerts")]
    [Description("Alerts Sentinel raised (symbol, kind, message, timestamp), newest first. days (7) · limit (50)")]
    public Task<string> NexusAlerts(
        int days = 7,
        int limit = 50)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Max(1, days)).ToString("yyyy-MM-ddTHH:mm:ssZ");
        return gw.GetAsync("nexus", $"/api/nexus/sentinel/alerts?since={Uri.EscapeDataString(since)}&limit={limit}");
    }

    [McpServerTool(Name = "san_people")]
    [Description("Contacts. For \"who is X\", \"what's X's number\", \"whose birthday is coming up\" (query birthdays). Returns ids. query*")]
    public Task<string> SanPeople(
        string query) =>
        query.Trim().Equals("birthdays", StringComparison.OrdinalIgnoreCase)
            ? gw.GetAsync("san", "/api/people/birthdays")
            : gw.GetAsync("san", $"/api/people?q={Uri.EscapeDataString(query)}");

    [McpServerTool(Name = "maaya_status")]
    [Description("Health of all 8 modules with latency. Call only AFTER a tool errors, to check if that module is down. Never speculatively.")]
    public Task<string> MaayaStatus() => gw.ProbeAllAsync(Probes);
}
