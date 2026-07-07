namespace San.Domain.Entities;

public class ActivitySnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Source { get; set; } = "manual"; // "iphone", "manual"
    public string Category { get; set; } = "health"; // "health", "location", "calendar"
    public string DataJson { get; set; } = "{}";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
