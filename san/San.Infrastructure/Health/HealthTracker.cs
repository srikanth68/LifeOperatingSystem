using System.Text.Json;
using Microsoft.Extensions.Logging;
using San.Application.Interfaces;

namespace San.Infrastructure.Health;

// One Settings row per component, holding its JSON state. Settings already exists,
// is already shared between the API and worker containers, and carries so little
// traffic that a write per worker tick is free next to what the tick itself does.
public class HealthTracker(ISanRepository repo, ILogger<HealthTracker> logger) : IHealthTracker
{
    private const string Prefix = "health.";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private sealed class State
    {
        public DateTime? LastOkAt { get; set; }
        public DateTime? LastFailAt { get; set; }
        public int ConsecutiveFailures { get; set; }
        public string? LastError { get; set; }
    }

    public async Task RecordAsync(string component, bool ok, string? error = null, CancellationToken ct = default)
    {
        try
        {
            var key = Prefix + component;
            var state = Parse(await repo.GetSettingAsync(key)) ?? new State();

            if (ok)
            {
                state.LastOkAt = DateTime.UtcNow;
                state.ConsecutiveFailures = 0;
                state.LastError = null;
            }
            else
            {
                state.LastFailAt = DateTime.UtcNow;
                state.ConsecutiveFailures += 1;
                // Truncated: this is a status line, not a log. The real exception has
                // already gone to the logger at the call site.
                state.LastError = error is { Length: > 300 } ? error[..300] : error;
            }

            await repo.SetSettingAsync(key, JsonSerializer.Serialize(state));
        }
        catch (Exception ex)
        {
            // The whole point of this class is to make failures visible; it must not
            // become a new way for them to happen.
            logger.LogDebug(ex, "Health bookkeeping failed for {Component} — ignoring.", component);
        }
    }

    public async Task<IReadOnlyList<ComponentHealth>> ReadAllAsync(CancellationToken ct = default)
    {
        var all = await repo.GetSettingsByPrefixAsync(Prefix);
        return all
            .Select(kv =>
            {
                var s = Parse(kv.Value) ?? new State();
                return new ComponentHealth(
                    kv.Key[Prefix.Length..], s.LastOkAt, s.LastFailAt, s.ConsecutiveFailures, s.LastError);
            })
            .OrderBy(c => c.Component, StringComparer.Ordinal)
            .ToList();
    }

    private static State? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JsonSerializer.Deserialize<State>(raw, Json); }
        catch (JsonException) { return null; }
    }
}
