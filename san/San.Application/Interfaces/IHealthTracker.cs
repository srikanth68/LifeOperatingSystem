namespace San.Application.Interfaces;

// What a component's recent history looks like. ConsecutiveFailures is the field
// worth alerting on: a single failed NorthStar write is noise, twelve in a row means
// San has been quietly forgetting everything it learned since.
public record ComponentHealth(
    string Component,
    DateTime? LastOkAt,
    DateTime? LastFailAt,
    int ConsecutiveFailures,
    string? LastError);

// Records what actually happened at San's edges, so the health endpoint reports
// observed behaviour rather than a fresh probe that only proves the wire is up right
// now. A NorthStar write can fail for an hour and then succeed the instant a probe
// asks — the counter is what remembers the hour.
//
// Backed by the Settings table rather than memory on purpose: San.API and San.Worker
// are SEPARATE containers sharing one SQLite file, and everything worth reporting
// (worker heartbeats, NorthStar write failures) happens in the worker while the
// endpoint being asked lives in the API.
public interface IHealthTracker
{
    // Best-effort by contract — health bookkeeping must never be the reason a real
    // operation fails.
    Task RecordAsync(string component, bool ok, string? error = null, CancellationToken ct = default);

    Task<IReadOnlyList<ComponentHealth>> ReadAllAsync(CancellationToken ct = default);
}

// Component names, shared so the writer and the reader cannot drift.
public static class HealthComponents
{
    public const string NorthStarWrite = "northstar.write";
    public const string NorthStarRecall = "northstar.recall";

    public const string WorkerAudit = "worker.audit";
    public const string WorkerEmailTriage = "worker.email_triage";
    public const string WorkerMemoryDistillation = "worker.memory_distillation";
    public const string WorkerCalendarSync = "worker.calendar_sync";
    public const string WorkerNotifications = "worker.notifications";
    public const string WorkerInsights = "worker.insights";
    public const string WorkerCommitments = "worker.commitments";
}
