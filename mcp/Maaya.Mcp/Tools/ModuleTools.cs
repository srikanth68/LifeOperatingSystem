using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Maaya.Mcp.Tools;

// Cross-module and derived read tools: the agenda, the one search, Vitara's
// conclusions, contacts, and module health. Each module's own records are read through
// its single *_read tool in ModuleReadTools. All calls are JWT-authenticated
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

    // The derived layer, not the raw one. vitara_read reports last night's numbers;
    // this reports what they mean against sixty days of the user's own history -- what
    // is off baseline, what has stepped to a new level, what is drifting.
    [McpServerTool(Name = "health_findings")]
    [Description("What Vitara has CONCLUDED about the user's health: what is off their personal baseline, early illness signs, sustained changes, drift, sleep debt, and missing data. Computed in code, not guessed. For \"is anything off\", \"how am I really doing\", \"anything I should know\". READ THE status FIELD FIRST - an empty findings list can mean the analysis has never run, which is NOT the same as the user being fine. Raw last-night numbers -> vitara_read.")]
    public Task<string> HealthFindings() => gw.GetAsync("insight", "/api/health/summary");

    [McpServerTool(Name = "health_baselines")]
    [Description("What is NORMAL for this user per metric - mean, spread, sample size, and whether there is enough data to be valid. Use before calling a reading high or low, so \"high\" means high FOR THEM rather than against a population.")]
    public Task<string> HealthBaselines() => gw.GetAsync("insight", "/api/health/baselines");

    // The only tool here that makes a claim about the future, and the only one whose
    // answer can be a refusal. The method field is load-bearing: Vitara ships the naive
    // answer whenever its fitted model failed to beat "tomorrow is like today", and a
    // caller that reports that as a prediction is inventing a forecast the system
    // explicitly declined to make.
    [McpServerTool(Name = "health_forecast")]
    [Description("Tomorrow's predicted readiness, resting heart rate, HRV and time asleep, each with the range it usually lands in. For \"how will I feel tomorrow\", \"should I train tomorrow\", \"is it worth setting an early alarm\". READ THE method FIELD PER METRIC: \"model\" is a fitted forecast that beat the alternatives on this user's own history; \"today\" means nothing beat assuming tomorrow is like today, so say it is expected to be much like today and do NOT present it as a prediction. The verdict field says how wrong each method has actually been. Last night's numbers -> vitara_read.")]
    public Task<string> HealthForecast() => gw.GetAsync("insight", "/api/health/forecast");

    // The portrait rather than the reading: standing properties of this body, measured
    // over months. Nothing in it is compared with a population, and the relationships
    // are associations in one person's history -- which the payload's own caveat says,
    // and which must survive being summarised.
    // The sheet for an appointment, and the right answer to most "should I be worried"
    // questions: not reassurance and not a diagnosis, but what changed, how long it has
    // been true, and what to ask someone qualified.
    [McpServerTool(Name = "health_visit_brief")]
    [Description("What to take to a doctor: what is outside a reference range or off this user's own usual, how long each has been true, and the questions those facts raise. For \"should I see a doctor\", \"what should I ask my GP\", \"is this worth worrying about\", \"prepare me for my appointment\". NOT a diagnosis and NOT advice - report what it says and nothing beyond it. It also lists what the system cannot see (symptoms, medications, family history, any examination) and which measurements have no baseline yet: never let an empty list read as reassurance.")]
    public Task<string> HealthVisitBrief() => gw.GetAsync("insight", "/api/health/visit-brief");

    [McpServerTool(Name = "health_signature")]
    [Description("How this user's body runs, from months of their own data: when they sleep and how far free nights drift, which weekday is weakest, how long they take to come back from a hard day, and what their numbers move with. For \"what kind of sleeper am I\", \"which day is my worst\", \"how long do I take to recover\", \"what affects my sleep\". Relationships here moved TOGETHER in their data - say so rather than implying cause. Each part is absent until there is enough history, and absent means not enough data, not zero. Today's readings -> vitara_read; what is off baseline -> health_findings.")]
    public Task<string> HealthSignature() => gw.GetAsync("insight", "/api/health/signature");

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
