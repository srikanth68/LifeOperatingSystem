using System.Diagnostics;
using System.Globalization;
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
        var mcpSecuredTask = ProbeMcpSecuredAsync(ct);
        var modulesTask = ProbeModulesAsync(ct);
        var llmTask = ProbeLlmAsync(ct);
        var emailTask = ProbeEmailAsync();
        var observedTask = health.ReadAllAsync(ct);
        await Task.WhenAll(mcpTask, mcpSecuredTask, modulesTask, llmTask, emailTask, observedTask);

        var mcpResult = await mcpTask;
        var mcpSecured = await mcpSecuredTask;
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

        if (mcpSecured is false)
            problems.Add(new(HealthProblemKeys.McpUnsecured, "critical",
                "The MCP gateway is running with no API key — every tool that can write to your modules is open to anything on the mesh."));

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
        if (ProbeBackup() is { } backupProblem) problems.Add(backupProblem);

        foreach (var a in accounts.Where(a => a.Stale))
            problems.Add(new(HealthProblemKeys.EmailAccount(a.EmailAddress), "medium",
                $"{a.EmailAddress} has not been checked since " +
                $"{(a.LastCheckedAt is { } t ? t.ToString("u") : "never")} — its token may have expired."));

        return new HealthReport(
            problems.Count == 0, problems, sw.ElapsedMilliseconds,
            new HealthProbes(mcpResult, mcpSecured, llm, modules, telegram.IsConfigured, accountCount, accounts),
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

    // The gateway's own /health reports whether its API key is configured. It is
    // unauthenticated by design (that is what makes it a probe target), and it is the
    // only way to notice from outside that the gateway is running without a key —
    // which, before it started refusing, meant ~41 write-capable tools open to anything
    // on the mesh. Returns null when it cannot be determined, which must not be
    // reported as insecure.
    private async Task<bool?> ProbeMcpSecuredAsync(CancellationToken ct)
    {
        var baseUrl = (Environment.GetEnvironmentVariable("MCP_BASE_URL") ?? "http://mcp:5900").TrimEnd('/');
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProbeTimeout);
        try
        {
            using var http = new HttpClient();
            using var resp = await http.GetAsync($"{baseUrl}/health", cts.Token);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
            return doc.RootElement.TryGetProperty("secured", out var s)
                   && s.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
                ? s.GetBoolean()
                : null;
        }
        catch
        {
            return null;
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

    // The nightly backup runs on the HOST, outside Docker, so nothing in the stack
    // would ever notice it stopping — and a backup discovered to have been broken for
    // three weeks is the same as no backup. It leaves a status file in the data
    // directory, which every container sees at /data/backup-status.json.
    //
    // Silent when the file is absent: a deployment that has not set the launchd job up
    // yet should not be nagged every fifteen minutes about a feature it never enabled.
    // Once the file exists, its absence of updates is a real problem.
    private static HealthProblem? ProbeBackup()
    {
        var path = Environment.GetEnvironmentVariable("BACKUP_STATUS_PATH") ?? "/data/backup-status.json";
        if (!File.Exists(path)) return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            var ok = !root.TryGetProperty("ok", out var okEl) || okEl.ValueKind != System.Text.Json.JsonValueKind.False;
            if (!ok)
            {
                var err = root.TryGetProperty("error", out var e) ? e.GetString() : null;
                return new HealthProblem(HealthProblemKeys.Backup, "critical",
                    $"The last backup FAILED{(string.IsNullOrWhiteSpace(err) ? "" : $": {err}")}.");
            }

            if (!root.TryGetProperty("lastRunUtc", out var lastEl) ||
                !DateTime.TryParse(lastEl.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var last))
                return null;

            var age = DateTime.UtcNow - last;
            // Nightly job, so 48h means two consecutive nights were missed — past the
            // point where it could be a one-off.
            if (age > TimeSpan.FromHours(48))
                return new HealthProblem(HealthProblemKeys.Backup, "high",
                    $"No successful backup in {age.TotalDays:F1} days — the nightly job has stopped.");

            return null;
        }
        catch
        {
            // A malformed status file is not evidence of a failed backup, and guessing
            // either way would be worse than saying nothing.
            return null;
        }
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
