namespace San.Domain.Entities;

// One row per distinct thing San has told the user about, keyed by a stable topic
// key the model supplies (e.g. "bill.amc.2026-08-09", "vault.cash_low").
//
// This exists because asking the model to remember what it already said does not
// work: told "don't repeat yourself", it rewords and escalates instead of going
// quiet, so the same $25 bill arrived seven times with rising urgency. Suppression
// has to be deterministic and outside the model.
//
// Shared by the system audit and email triage — one fact reported by both notifies
// once, because they share this key namespace.
public class NotificationLedgerEntry
{
    public string Key { get; set; } = "";
    public string Severity { get; set; } = "medium";
    public string LastMessage { get; set; } = "";
    public string Source { get; set; } = "";      // "audit" | "email"
    public int NotifyCount { get; set; }
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastNotifiedAt { get; set; } = DateTime.UtcNow;
    // Model-supplied deadline, when the finding has one. Near a deadline the
    // backoff is suspended so a due bill keeps its steady cadence.
    public DateTime? DueOn { get; set; }
}
