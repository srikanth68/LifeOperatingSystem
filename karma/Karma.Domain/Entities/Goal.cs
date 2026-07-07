namespace Karma.Domain.Entities;

public class Goal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Category { get; set; } = "personal";
    public string Status { get; set; } = "active";
    public int Progress { get; set; } = 0;
    public DateOnly? TargetDate { get; set; }
    public string? LinksJson { get; set; }   // [{label, url}]
    public string? Resources { get; set; }
    public string? Tags { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
