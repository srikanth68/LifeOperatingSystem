namespace NorthStar.Domain.Entities;

// A single discrete thing that happened in a module, with its REAL occurrence time.
// Unlike KnowledgeEntry (one distilled state-snapshot per module per day, upserted),
// this table is append-only and event-level: one row per Vault transaction, per Karma
// habit check-in, per reminder fired, per property added, per document uploaded, etc.
// This is what lets San do genuine time-series reasoning ("since your last message,
// three transactions posted and a reminder fired at 4:10pm").
public class ActivityEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Which module the event came from: vault, vitara, aasthi, karma, sutra, nexus, san…
    public string Source { get; set; } = "";

    // The kind of event, module-defined but stable: "transaction", "habit_checkin",
    // "reminder_fired", "property_added", "document_uploaded", "goal_completed"…
    public string Kind { get; set; } = "";

    // Human-readable one-liner ("Starbucks — $6.40", "Morning run checked in").
    public string Title { get; set; } = "";

    // Optional extra detail (category, amount, target value…).
    public string? Detail { get; set; }

    // When the event ACTUALLY happened in the real world (module-supplied). This is the
    // axis San reasons over — NOT when NorthStar recorded it.
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    // When NorthStar persisted the row (bookkeeping / harvest lag visibility).
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    // Idempotency key, natural + stable per real-world event, e.g. "vault:transaction:<txnId>".
    // A UNIQUE index on this makes ingestion safe to repeat: the 15-min harvester (and any
    // retrying POSTer) can re-submit the same event and it lands exactly once.
    public string EventKey { get; set; } = "";

    // Optional raw payload the producer attached, for later richer reasoning.
    public string? RawJson { get; set; }
}
