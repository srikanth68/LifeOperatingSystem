namespace NorthStar.Domain.Entities;

public class ActionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Source { get; set; } = "manual";
    public string Category { get; set; } = "task";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int Priority { get; set; } = 3;
    public DateOnly? DueDate { get; set; }
    public string Status { get; set; } = "pending";
    public string? ResolvedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
