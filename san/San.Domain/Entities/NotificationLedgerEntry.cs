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
//
// SEEN vs NOTIFIED vs RECORDED are three different things and are tracked apart:
//   seen     — the finding came back in a run (every 15 min while it stays true)
//   notified — it cleared the Telegram cooldown and the user was actually told
//   recorded — NorthStar was given the finding, so San's brain knows it
// They used to be collapsed into "notified", which meant a finding that changed
// while inside its cooldown was dropped whole and NorthStar never learned it had
// changed. See KnowledgePolicy.
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

    // Every sighting, whether or not it was worth telling anyone about. Makes the
    // difference between "quiet because nothing is wrong" and "quiet because the
    // cooldown is swallowing it" visible in the ledger.
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public int SeenCount { get; set; }

    // What NorthStar was last told, and when. Compared against on later sightings
    // to decide whether the brain is out of date — an empty KnowledgeMessage means
    // the brain has never heard about this key at all.
    public DateTime KnowledgeAt { get; set; }
    public string KnowledgeMessage { get; set; } = "";
}
