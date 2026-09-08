namespace San.Application.DTOs;

// One thing Vitara has concluded about the user's health.
//
// A deliberate subset of Vitara's own Finding. San does not need the evidence blob or
// the raw z-scores to decide whether to tell the user something -- it needs to know
// what happened, how serious it is, how long it has been true, and a key stable enough
// to deduplicate against. Carrying the rest across the module boundary would mean two
// copies of a schema that has to agree, for data nothing on this side reads.
public record HealthFinding(
    string Key,
    string Type,
    string Severity,
    string Summary,

    // How many consecutive days this has been detected. The difference between "your
    // resting heart rate is up this morning" and "for the fifth morning running", and
    // the reason it is worth carrying at all.
    int DaysRunning);
