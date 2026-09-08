using San.Application.DTOs;

namespace San.Application;

// Turning Vitara's conclusions into the shape San's notification ledger speaks.
//
// In Application rather than in the worker that calls it, for the same reason
// WriteClaimCheck and NotifyPolicy are: this is a policy decision about what the user
// gets told, and policy that lives inside a BackgroundService cannot be tested without
// standing one up.
public static class HealthFindingMapping
{
    // Vitara grades info | notable | high. The ledger speaks low | medium | high |
    // critical.
    //
    // NOTHING maps to critical, deliberately. This system infers from a ring and sixty
    // days of history; a wearable is not equipped to declare an emergency, and a
    // "critical" health alert from one would be both frightening and unfounded. The
    // most it should ever do is say something is worth a look.
    public static string Severity(string vitaraSeverity) => vitaraSeverity switch
    {
        "high" => "high",
        "notable" => "medium",
        _ => "low",
    };

    // How long it has been true belongs in the sentence. "For the fifth morning
    // running" is what turns a reading into something worth acting on, and it is the
    // whole reason a continuing finding keeps its original detection date instead of
    // being rewritten as new each day.
    public static string Message(HealthFinding f) =>
        f.DaysRunning <= 1 ? f.Summary : $"{f.Summary} (day {f.DaysRunning})";

    // The key crosses the module boundary UNCHANGED. Vitara derives it from content in
    // code so the same condition produces the same key on every run; rederiving or
    // decorating it here would silently break the deduplication it exists for, and the
    // symptom would be one notification per morning forever.
    public static AgentFinding ToAgentFinding(HealthFinding f) =>
        new(f.Key, Severity(f.Severity), Message(f), DueOn: null);
}
