namespace Aasthi.Domain.Entities;

public class PropertyTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PropertyId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public string Status { get; set; } = "pending";   // pending | in_progress | completed | cancelled
    public string Priority { get; set; } = "medium";  // low | medium | high | urgent
    public string Source { get; set; } = "manual";     // manual | san | email
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public Property Property { get; set; } = null!;
}
