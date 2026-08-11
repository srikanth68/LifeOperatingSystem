using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using San.Application.Interfaces;
using San.Infrastructure.Agent;

namespace San.API.Controllers;

// "What is San actually running on right now?"
//
// San is built almost entirely out of quiet fallbacks, and that is the right design —
// chat should keep answering when NorthStar is down and when the MCP gateway is
// unreachable. The cost is that degradation is invisible: the MCP catalogue silently
// dropped from ~41 tools to the 10 built-ins and stayed that way until San started
// failing at things it obviously had tools for. Nothing reported it, because nothing
// was broken enough to report.
//
// So this reports two different kinds of truth and does not confuse them:
//   PROBES   — asked live, right now. Proves the wire is up this second.
//   OBSERVED — counters written at the moment things actually happened, by whichever
//              container was doing the work. A NorthStar write can fail for an hour
//              and then succeed the instant a probe asks; the counter remembers the
//              hour.
[ApiController, Route("api/health")]
public class HealthController(
    ISanRepository repo,
    IHealthTracker health,
    IChatProvider chat,
    ITelegramNotifier telegram,
    McpToolClient mcp,
    IHttpClientFactory httpFactory) : ControllerBase
{
    // Liveness only. Deliberately anonymous and free of dependencies so it answers
    // even when everything below it is broken — a container orchestrator asking "is
    // this process up" must not get a 500 because NorthStar is down.
    [HttpGet, AllowAnonymous]
    public IActionResult Get() => Ok(new { status = "ok", module = "san", utc = DateTime.UtcNow });

    [HttpGet("deep")]
    public async Task<IActionResult> Deep(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // All probes in parallel with their own short timeout: a health check that
        // hangs because a dependency hangs is worse than no health check, since it
        // fails exactly when you most need an answer.
        var mcpTask = ProbeMcpAsync(ct);
        var modulesTask = ProbeModulesAsync(ct);
        var llmTask = ProbeLlmAsync(ct);
        var emailTask = ProbeEmailAsync();
        var observedTask = health.ReadAllAsync(ct);

        await Task.WhenAll(mcpTask, modulesTask, llmTask, emailTask, observedTask);

        var mcpResult = await mcpTask;
        var modules = await modulesTask;
        var observed = await observedTask;

        var problems = new List<string>();
        if (!mcpResult.Ok)
            problems.Add("MCP gateway unreachable — San is running on built-in tools only.");
        else if (mcpResult.ToolCount <= BuiltInToolCount)
            problems.Add($"MCP returned only {mcpResult.ToolCount} tools — expected the full catalogue.");
        problems.AddRange(modules.Where(m => !m.Ok).Select(m => $"{m.Name} unreachable ({m.Detail})."));
        if (!(await llmTask).Ok) problems.Add("LLM endpoint unreachable — chat will fail.");
        problems.AddRange(observed
            .Where(c => c.ConsecutiveFailures >= 3)
            .Select(c => $"{c.Component} has failed {c.ConsecutiveFailures}x in a row ({c.LastError})."));
        problems.AddRange(StaleWorkers(observed));

        return Ok(new
        {
            healthy = problems.Count == 0,
            problems,
            checkedInMs = sw.ElapsedMilliseconds,
            probes = new
            {
                mcp = mcpResult,
                llm = await llmTask,
                modules,
                telegram = new { configured = telegram.IsConfigured },
                email = await emailTask,
            },
            observed,
        });
    }

    // The AgentToolRegistry fallback. Seeing this number rather than the full catalogue
    // is the exact symptom that went unnoticed for days.
    private const int BuiltInToolCount = 10;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private record ProbeResult(bool Ok, string? Detail = null);
    private record McpResult(bool Ok, int ToolCount, string? Detail);
    private record ModuleResult(string Name, bool Ok, string? Detail);

    // A cancelled HttpClient call surfaces as "A task was canceled." regardless of why,
    // which tells the reader nothing — a module that is DOWN and one that is merely
    // SLOW need different responses from them.
    private static string Describe(Exception ex, CancellationTokenSource probeCts, CancellationToken requestCt)
    {
        if (ex is OperationCanceledException && probeCts.IsCancellationRequested && !requestCt.IsCancellationRequested)
            return $"no response within {ProbeTimeout.TotalSeconds:F0}s";
        return ex.GetBaseException().Message;
    }

    private async Task<McpResult> ProbeMcpAsync(CancellationToken ct)
    {
        try
        {
            var tools = await mcp.TryListToolsAsync(ct);
            return tools is null
                ? new McpResult(false, 0, "gateway did not answer — using built-in registry")
                : new McpResult(true, tools.Count, null);
        }
        catch (Exception ex)
        {
            return new McpResult(false, 0, ex.Message);
        }
    }

    private static readonly string[] Siblings =
        ["vault", "vitara", "aasthi", "northstar", "sutra", "karma", "nexus"];

    private async Task<List<ModuleResult>> ProbeModulesAsync(CancellationToken ct)
    {
        var tasks = Siblings.Select(async name =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProbeTimeout);
            try
            {
                var http = httpFactory.CreateClient(name);
                using var resp = await http.GetAsync("/", cts.Token);
                // ANY HTTP response means the module answered. 401 and 404 are healthy
                // here — every module enforces auth, and reachability is the question,
                // not authorisation.
                return new ModuleResult(name, true, $"HTTP {(int)resp.StatusCode}");
            }
            catch (Exception ex)
            {
                return new ModuleResult(name, false, Describe(ex, cts, ct));
            }
        });
        return (await Task.WhenAll(tasks)).ToList();
    }

    private async Task<ProbeResult> ProbeLlmAsync(CancellationToken ct)
    {
        var baseUrl = Environment.GetEnvironmentVariable("LLM_BASE_URL")
                      ?? Environment.GetEnvironmentVariable("LLAMACPP_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            return new ProbeResult(true, $"{chat.ProviderName} (no local endpoint configured)");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProbeTimeout);
        try
        {
            using var http = new HttpClient();
            using var resp = await http.GetAsync($"{baseUrl.TrimEnd('/')}/v1/models", cts.Token);
            return new ProbeResult(resp.IsSuccessStatusCode,
                $"{chat.ProviderName}/{chat.ModelName} — HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            return new ProbeResult(false, Describe(ex, cts, ct));
        }
    }

    private async Task<object> ProbeEmailAsync()
    {
        var accounts = await repo.GetEmailAccountsAsync();
        return new
        {
            count = accounts.Count,
            active = accounts.Count(a => a.Active),
            accounts = accounts.Select(a => new
            {
                a.EmailAddress,
                a.Provider,
                a.Active,
                a.LastCheckedAt,
                // An OAuth refresh token that quietly stopped working looks exactly
                // like a mailbox with no new mail, which is why this is worth surfacing.
                stale = a.Active && (a.LastCheckedAt is null || DateTime.UtcNow - a.LastCheckedAt > TimeSpan.FromHours(2)),
            }),
        };
    }

    // A worker whose timer died is completely undetectable today: no error, no log, the
    // system simply goes quiet and quiet is indistinguishable from "nothing to report".
    private static IEnumerable<string> StaleWorkers(IReadOnlyList<ComponentHealth> observed)
    {
        var expected = new Dictionary<string, TimeSpan>
        {
            [HealthComponents.WorkerAudit] = TimeSpan.FromMinutes(45),
            [HealthComponents.WorkerEmailTriage] = TimeSpan.FromMinutes(45),
            [HealthComponents.WorkerMemoryDistillation] = TimeSpan.FromMinutes(45),
        };

        foreach (var (component, maxGap) in expected)
        {
            var entry = observed.FirstOrDefault(c => c.Component == component);
            if (entry?.LastOkAt is null)
            {
                // Says nothing on a freshly started container, which is correct — it
                // has genuinely never run yet.
                if (entry is not null)
                    yield return $"{component} has never completed a run.";
                continue;
            }
            var age = DateTime.UtcNow - entry.LastOkAt.Value;
            if (age > maxGap)
                yield return $"{component} last ran {age.TotalHours:F1}h ago — its timer may have died.";
        }
    }
}
