namespace NorthStar.Domain.Entities;

// Agent long-term memory. The agent writes distilled
// observations/decisions here; recall is FTS5-ranked. NorthStar stores, the agent thinks.
public class MemoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Content { get; set; } = "";
    public string Kind { get; set; } = "observation"; // observation, preference, event, decision, skill
    public string Source { get; set; } = "agent";     // san, mcp, manual, <module>
    public string Tags { get; set; } = "";            // comma-separated
    public int Importance { get; set; } = 3;          // 1 trivial … 5 critical
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAccessedAt { get; set; }     // reinforcement: touched on every recall hit
    public int AccessCount { get; set; }
}
