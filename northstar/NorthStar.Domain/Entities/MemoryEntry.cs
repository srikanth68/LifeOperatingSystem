namespace NorthStar.Domain.Entities;

// Agent long-term memory. The agent writes distilled
// observations/decisions here; recall is FTS5-ranked. NorthStar stores, the agent thinks.
public class MemoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Content { get; set; } = "";
    public string Kind { get; set; } = "observation"; // see MemoryKinds
    public string Source { get; set; } = "agent";     // san, mcp, manual, <module>
    public string Tags { get; set; } = "";            // comma-separated
    public int Importance { get; set; } = 3;          // 1 trivial … 5 critical
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAccessedAt { get; set; }     // reinforcement: touched on every recall hit
    public int AccessCount { get; set; }

    // ── Lifecycle (see MemoryStatus) ──
    // Nothing is overwritten. A correction is a new memory that supersedes the old one,
    // and the old one stays, marked, so "what did we believe before?" still has an answer.
    public string Status { get; set; } = MemoryStatus.Active;
    // The memory this one replaces. While this one is only proposed and the target is
    // active, the replacement is pending: an unconfirmed claim never displaces a
    // confirmed one. It applies when this memory is confirmed.
    public Guid? Supersedes { get; set; }
    public Guid? SupersededBy { get; set; }
    public DateTime? StatusChangedAt { get; set; }
    public string? StatusNote { get; set; }

    // ── Provenance ──
    // Where it came from ("san:chat:<first>..<last>", "email:...", "event:<key>") and the
    // words that justify it, kept with the memory so "why do you think that?" has an
    // answer after the conversation or email itself is gone.
    public string? SourceRef { get; set; }
    public string? Evidence { get; set; }

    // Normalised content, so the same memory is not stored twice. Maintained on write.
    public string ContentKey { get; set; } = "";
}

public static class MemoryKinds
{
    // "fact" was missing here while San's distiller asked the model for it, so every
    // fact it extracted was refused with a 400 and lost.
    public static readonly string[] All = ["observation", "preference", "fact", "event", "decision", "skill"];

    public static bool IsValid(string? kind) => kind is not null && All.Contains(kind);
}

public static class MemoryStatus
{
    // A claim an agent made that nobody has confirmed. Still recalled, labelled as
    // unconfirmed, so San benefits from it without treating it as settled.
    public const string Proposed = "proposed";
    // Confirmed by the owner, written by the owner, or stated by them directly.
    public const string Active = "active";
    // Replaced by a newer memory (SupersededBy). Kept for history, never recalled.
    public const string Superseded = "superseded";
    // Withdrawn as wrong or unwanted. Kept for history, never recalled.
    public const string Retracted = "retracted";

    public static readonly string[] All = [Proposed, Active, Superseded, Retracted];

    // What recall and San may see.
    public static bool IsLive(string? status) => status is Proposed or Active;
}
