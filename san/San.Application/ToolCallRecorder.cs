using System.Diagnostics;
using System.Text.Json;
using San.Application.Interfaces;

namespace San.Application;

// One tool call, as it happened.
public record ToolCallRecord(string Name, string Arguments, string Result, bool Ok, long Ms);

// Watches the agent loop execute tools, without changing what it does.
//
// The provider owns the loop and already knows every tool it ran -- but it knows it
// privately, throws it away at the end of the turn, and reaching into it would mean
// changing IChatProvider for every implementation. The executor, though, is a delegate
// the CALLER supplies. Wrapping it observes every call from outside the loop, and the
// provider cannot tell the difference.
//
// Three rules, all of them about not becoming the reason a turn fails:
//   - exceptions pass straight through, recorded but never swallowed
//   - results are clipped, because a transaction list is megabytes and the shape is
//     what matters, not the payload
//   - nothing here is awaited on the critical path beyond the call it is already making
public sealed class ToolCallRecorder
{
    // Enough to see what was asked for and what came back. A full result would make the
    // dataset mostly Vault transaction JSON, which teaches a model nothing about which
    // tool to call.
    private const int MaxArguments = 600;
    private const int MaxResult = 600;

    private readonly List<ToolCallRecord> _calls = [];

    public IReadOnlyList<ToolCallRecord> Calls => _calls;

    // The names, in the shape WriteClaimCheck wants. This is what makes a turn's guard
    // verdict reproducible after the fact rather than only inside the provider.
    public IReadOnlyList<string> Names => _calls.Select(c => c.Name).ToList();

    public Func<ToolCall, CancellationToken, Task<string>> Wrap(
        Func<ToolCall, CancellationToken, Task<string>> inner)
        => async (call, ct) =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = await inner(call, ct);
                Record(call, Clip(result, MaxResult), ok: true, sw.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                // A failing tool is one of the more interesting things that can happen
                // in a turn -- it is often why the reply that follows is wrong -- so it
                // is recorded and then rethrown exactly as it arrived.
                Record(call, Clip(ex.Message, MaxResult), ok: false, sw.ElapsedMilliseconds);
                throw;
            }
        };

    private void Record(ToolCall call, string result, bool ok, long ms)
    {
        var args = call.Arguments.Count == 0 ? "{}" : JsonSerializer.Serialize(call.Arguments);
        _calls.Add(new ToolCallRecord(call.Name, Clip(args, MaxArguments), result, ok, ms));
    }

    public string ToJson() => JsonSerializer.Serialize(_calls);

    private static string Clip(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "…";
    }
}
