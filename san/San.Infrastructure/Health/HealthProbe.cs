using System.Diagnostics;
using San.Application.Interfaces;
using San.Infrastructure.Agent;

namespace San.Infrastructure.Health;

// Answers "what is San actually running on right now?" and, separately, "what has
// been going wrong while nobody was asking?".
//
// San is built almost entirely out of quiet fallbacks, and that is the right design —
// chat should keep answering when NorthStar is down and when the MCP gateway is
// unreachable. The cost is that degradation is invisible: the MCP catalogue dropped
// from ~41 tools to the 10 built-ins and stayed there until San started visibly
// failing at things it obviously had tools for. Nothing reported it, because nothing
// was broken enough to report.
public class HealthProbe(
    ISanRepository repo,
    IHealthTracker health,
    IChatProvider chat,
    ITelegramNotifier telegram,
    McpToolClient mcp,
    IHttpClientFactory httpFactory) : IHealthProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    // The AgentToolRegistry fallback size. Seeing this number rather than the full
    // catalogue is the exact symptom that went unnoticed for days.
    private const int BuiltInToolCount = 10;

    private static readonly string[] Siblings =
        ["vault", "vitara", "aasthi", "northstar", "sutra", "karma", "nexus"];

    // Losing NorthStar costs San its memory; losing any other module only costs a
    // block of context in one prompt.
    private static readonly HashSet<string> CriticalModules = ["northstar"];

    public async Task<HealthReport> RunAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // All probes in parallel, each with its own short timeout: a health check that
        // hangs because a dependency hangs is worse than none, since it fails exactly
        // when you most need an answer.
        var mcpTask = ProbeMcpAsync(ct);
        var modulesTask = ProbeModulesAsync(ct);
        var llmTask = ProbeLlmAsync(ct);
        var emailTask = ProbeEmailAsync();
        var observedTask = health.ReadAllAsync(ct);
        await Task.WhenAll(mcpTask, modulesTask, llmTask, emailTask, observedTask);

        var mcpResult = await mcpTask;
        var llm = await llmTask;
        var modules = await modulesTask;
        var (accountCount, accounts) = await emailTask;
        var observed = await observedTask;

        var problems = new List<HealthProblem>();

        if (!mcpResult.Ok)
            problems.Add(new(HealthProblemKeys.Mcp, "high",
                "MCP gateway is unreachable — San is running on built-in tools only."));
        else if (mcpResult.ToolCount <= BuiltInToolCount)
            problems.Add(new(HealthProblemKeys.Mcp, "high",
                $"MCP is serving only {mcpResult.ToolCount} tools instead of the full catalogue."));

        if (!llm.Ok)
            problems.Add(new(HealthProblemKeys.Llm, "critical",
                $"The LLM endpoint is unreachable ({llm.Detail}) — chat will fail."));

        foreach (var m in modules.Where(m => !m.Ok))
            problems.Add(new(HealthProblemKeys.Module(m.Name),
                CriticalModules.Contains(m.Name) ? "high" : "medium",
                $"{m.Name} is unreachable ({m.Detail})."));

        foreach (var c in observed.Where(c => c.ConsecutiveFailures >= 3))
            problems.Add(new(HealthProblemKeys.Component(c.Component),
                c.ConsecutiveFailures >= 10 ? "critical" : "high",
                $"{c.Component} has failed {c.ConsecutiveFailures} times in a row ({c.LastError})."));

        problems.AddRange(StalledWorkers(observed));

        foreach (var a in accounts.Where(a => a.Stale))
            problems.Add(new(HealthProblemKeys.EmailAccount(a.EmailAddress), "medium",
                $"{a.EmailAddress} has not been checked since " +
                $"{(a.LastCheckedAt is { } t ? t.ToString("u") : "never")} — its token may have expired."));

        return new HealthReport(
            problems.Count == 0, problems, sw.ElapsedMilliseconds,
            new HealthProbes(mcpResult, llm, modules, telegram.IsConfigured, accountCount, accounts),
            observed);
    }

    private async Task<McpProbe> ProbeMcpAsync(CancellationToken ct)
    {
        try
        {
            var tools = await mcp.TryListToolsAsync(ct);
            return tools is null
                ? new McpProbe(false, 0, "gateway did not answer — using built-in registry")
                : new McpProbe(true, tools.Count, null);
        }
        catch (Exception ex)
        {
            return new McpProbe(false, 0, ex.GetBaseException().Message);
        }
    }

    private async Task<List<ServiceProbe>> ProbeModulesAsync(CancellationToken ct)
    {
        var tasks = Siblings.Select(async name =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProbeTimeout);
            try
            {
                var http = httpFactory.CreateClient(name);
                // A named client that was never registered comes back with no
                // BaseAddress. Reporting that as an outage would be a lie that no
                // amount of retrying clears — it's a wiring mistake, not a down
                // module, and it must not look like one.
                if (http.BaseAddress is null)
                    return new ServiceProbe(name, true, "not configured in this process");

                using var resp = await http.GetAsync("/", cts.Token);
                // ANY HTTP response means the module answered. 401 and 404 are healthy
                // here — every module enforces auth, and reachability is the question,
                // not authorisation.
                return new ServiceProbe(name, true, $"HTTP {(int)resp.StatusCode}");
            }
            catch (Exception ex)
            {
                return new ServiceProbe(name, false, Describe(ex, cts, ct));
            }
        });
        return (await Task.WhenAll(tasks)).ToList();
    }

    private async Task<ServiceProbe> ProbeLlmAsync(CancellationToken ct)
    {
        var baseUrl = Environment.GetEnvironmentVariable("LLM_BASE_URL")
                      ?? Environment.GetEnvironmentVariable("LLAMACPP_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            return new ServiceProbe("llm", true, $"{chat.ProviderName} (no local endpoint configured)");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProbeTimeout);
        try
        {
            using var http = new HttpClient();
            using var resp = await http.GetAsync($"{baseUrl.TrimEnd('/')}/v1/models", cts.Token);
            return new ServiceProbe("llm", resp.IsSuccessStatusCode,
                $"{chat.ProviderName}/{chat.ModelName} — HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            return new ServiceProbe("llm", false, Describe(ex, cts, ct));
        }
    }

    private async Task<(int Count, List<EmailAccountProbe> Accounts)> ProbeEmailAsync()
    {
        var accounts = await repo.GetEmailAccountsAsync();
        return (accounts.Count, accounts.Select(a => new EmailAccountProbe(
            a.EmailAddress, a.Provider, a.Active, a.LastCheckedAt,
            // An OAuth refresh token that quietly stopped working looks exactly like a
            // mailbox with no new mail, which is why this is worth surfacing.
            Stale: a.Active && (a.LastCheckedAt is null
                                || DateTime.UtcNow - a.LastCheckedAt > TimeSpan.FromHours(2)))).ToList());
    }

    // A worker whose timer died is otherwise undetectable: no error, no log, the system
    // simply goes quiet — and quiet is indistinguishable from "nothing to report".
    private static IEnumerable<HealthProblem> StalledWorkers(IReadOnlyList<ComponentHealth> observed)
    {
        var expected = new Dictionary<string, TimeSpan>
        {
            [HealthComponents.WorkerAudit] = TimeSpan.FromMinutes(45),
            [HealthComponents.WorkerEmailTriage] = TimeSpan.FromMinutes(45),
            [HealthComponents.WorkerMemoryDistillation] = TimeSpan.FromMinutes(45),
            [HealthComponents.WorkerCalendarSync] = TimeSpan.FromHours(26),
            [HealthComponents.WorkerNotifications] = TimeSpan.FromMinutes(45),
        };

        foreach (var (component, maxGap) in expected)
        {
            // Absent entirely means a freshly started container that has genuinely
            // never run yet — correctly silent rather than alarming on every restart.
            var entry = observed.FirstOrDefault(c => c.Component == component);
            if (entry is null) continue;

            if (entry.LastOkAt is null)
            {
                yield return new HealthProblem(HealthProblemKeys.Worker(component), "high",
                    $"{component} has never completed a run.");
                continue;
            }

            var age = DateTime.UtcNow - entry.LastOkAt.Value;
            if (age > maxGap)
                yield return new HealthProblem(HealthProblemKeys.Worker(component), "high",
                    $"{component} last completed a run {age.TotalHours:F1}h ago — its timer may have died.");
        }
    }

    // A cancelled HttpClient call surfaces as "A task was canceled." regardless of why,
    // which tells the reader nothing — a module that is DOWN and one that is merely
    // SLOW need different responses from them.
    private static string Describe(Exception ex, CancellationTokenSource probeCts, CancellationToken requestCt)
    {
        if (ex is OperationCanceledException && probeCts.IsCancellationRequested && !requestCt.IsCancellationRequested)
            return $"no response within {ProbeTimeout.TotalSeconds:F0}s";
        return ex.GetBaseException().Message;
    }
}
