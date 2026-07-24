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
