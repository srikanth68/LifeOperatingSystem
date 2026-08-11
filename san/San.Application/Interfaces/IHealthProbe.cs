namespace San.Application.Interfaces;

// A problem carries its own KEY and SEVERITY rather than being a bare sentence,
// because the same problem has to survive being reported twice: once as JSON on the
// health endpoint, and once as a keyed finding through the notification ledger. The
// key is what makes "MCP is degraded" at 09:00 and at 09:15 the same fact instead of
// two alerts, so it must be generated here, where the condition is detected, not
// derived from the wording afterwards.
public record HealthProblem(string Key, string Severity, string Message);

public record McpProbe(bool Ok, int ToolCount, string? Detail);
public record ServiceProbe(string Name, bool Ok, string? Detail);
public record EmailAccountProbe(
    string EmailAddress, string Provider, bool Active, DateTime? LastCheckedAt, bool Stale);

public record HealthProbes(
    McpProbe Mcp,
    ServiceProbe Llm,
    IReadOnlyList<ServiceProbe> Modules,
    bool TelegramConfigured,
    int EmailAccountCount,
    IReadOnlyList<EmailAccountProbe> EmailAccounts);

public record HealthReport(
    bool Healthy,
    IReadOnlyList<HealthProblem> Problems,
    long CheckedInMs,
    HealthProbes Probes,
    IReadOnlyList<ComponentHealth> Observed);

// Lives behind an interface in Application, not inside the API controller, because
// San.API and San.Worker are separate containers. The worker cannot ask the API over
// HTTP how healthy it is — that would fail to report precisely the failure most worth
// reporting, San.API being down.
public interface IHealthProbe
{
    Task<HealthReport> RunAsync(CancellationToken ct = default);
}

// Stable keys for conditions this detects. Same namespace as the model's finding keys
// (the ledger holds one keyed set for both), prefixed so a system problem can never
// collide with something the audit noticed about the user's life.
public static class HealthProblemKeys
{
    public const string Prefix = "health.";

    public static string Mcp => Prefix + "mcp";
    public static string Llm => Prefix + "llm";
    public static string Module(string name) => Prefix + "module." + name;
    public static string Component(string component) => Prefix + "component." + component;
    public static string Worker(string component) => Prefix + "stalled." + component;
    public static string EmailAccount(string address) => Prefix + "email." + address.ToLowerInvariant();
}
