namespace NorthStar.Domain.Entities;

public class KnowledgeEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Source { get; set; } = "manual"; // vault, vitara, aasthi, san, manual
    public string Topic { get; set; } = "";        // sleep, spending, property, task, health, general
    public string Summary { get; set; } = "";
    public string? RawJson { get; set; }
    public DateOnly? Day { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
