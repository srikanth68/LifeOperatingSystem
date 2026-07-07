namespace NorthStar.Domain.Entities;

public class Insight
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string GeneratedBy { get; set; } = "manual"; // ollama, gemini, manual
    public string? SourceEntryIds { get; set; } // comma-separated KnowledgeEntry IDs used
    public bool Dismissed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
