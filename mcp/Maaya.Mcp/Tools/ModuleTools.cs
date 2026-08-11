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

    [McpServerTool(Name = "maaya_search")]
    [Description(
        "Search across the user's ENTIRE Maaya system at once — documents (Sutra), property records, " +
        "tasks and maintenance (Aasthi), bank transactions (Vault), and long-term memory (NorthStar). " +
        "USE THIS whenever the user asks where something is or to find/recall something without saying " +
        "which module holds it — e.g. 'find the HVAC invoice', 'what did I spend at Home Depot', " +
        "'when did I last service the boiler'. Returns results grouped by source; a group is omitted " +
        "when it has no matches.")]
    public async Task<string> MaayaSearch(
        [Description("What to look for, e.g. 'HVAC', 'Home Depot', 'roof warranty'.")] string query)
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
    [Description("Vault — the user's finances: net worth, cash, debt, spending summary.")]
    public Task<string> VaultFinances() => gw.GetAsync("vault", "/api/summary");

    [McpServerTool(Name = "vitara_health")]
    [Description("Vitara — the user's health dashboard: readiness, sleep, activity, heart metrics (Oura-sourced).")]
    public Task<string> VitaraHealth() => gw.GetAsync("vitara", "/api/dashboard");

    [McpServerTool(Name = "aasthi_properties")]
    [Description("Aasthi — the user's real-estate portfolio: properties, values, profit.")]
    public Task<string> AasthiProperties() => gw.GetAsync("aasthi", "/api/properties");

    [McpServerTool(Name = "sutra_documents")]
    [Description("Sutra — the user's document vault. Without a query returns stats (counts by category, expiring soon); with a query returns matching documents.")]
    public Task<string> SutraDocuments(
        [Description("Optional free-text search over stored documents.")] string? query = null) =>
        string.IsNullOrWhiteSpace(query)
            ? gw.GetAsync("sutra", "/api/documents/stats")
            : gw.GetAsync("sutra", $"/api/documents?q={Uri.EscapeDataString(query)}");

    [McpServerTool(Name = "karma_habits")]
    [Description("Karma — today's habit check-ins and active goals with progress.")]
    public async Task<string> KarmaHabits()
    {
        var habits = await gw.GetAsync("karma", "/api/habits/today");
        var goals = await gw.GetAsync("karma", "/api/goals");
        return $"{{\"habitsToday\":{habits},\"goals\":{goals}}}";
    }

    [McpServerTool(Name = "nexus_market")]
    [Description("Nexus — trading monitor status: tracked symbols, recent alert COUNT, market open/closed. Read-only; never places trades. Use nexus_alerts to read the actual alerts.")]
    public Task<string> NexusMarket() => gw.GetAsync("nexus", "/api/nexus/sentinel/status");

    [McpServerTool(Name = "nexus_alerts")]
    [Description("Nexus — the actual alerts Sentinel has raised (symbol, kind, message, timestamp), newest first. Read-only; never places trades.")]
    public Task<string> NexusAlerts(
        [Description("How many days back to look (default 7).")] int days = 7,
        [Description("Max alerts to return (default 50).")] int limit = 50)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Max(1, days)).ToString("yyyy-MM-ddTHH:mm:ssZ");
        return gw.GetAsync("nexus", $"/api/nexus/sentinel/alerts?since={Uri.EscapeDataString(since)}&limit={limit}");
    }

    [McpServerTool(Name = "san_people")]
    [Description("San — the user's people/contacts. Search by name, or list upcoming birthdays with query 'birthdays'.")]
    public Task<string> SanPeople(
        [Description("Name to search, or 'birthdays' for upcoming birthdays.")] string query) =>
        query.Trim().Equals("birthdays", StringComparison.OrdinalIgnoreCase)
            ? gw.GetAsync("san", "/api/people/birthdays")
            : gw.GetAsync("san", $"/api/people?q={Uri.EscapeDataString(query)}");

    [McpServerTool(Name = "maaya_status")]
    [Description("Health check across all 8 Maaya modules — who is online, with latency. Call when a module tool errored or before a multi-module task.")]
    public Task<string> MaayaStatus() => gw.ProbeAllAsync(Probes);
}
