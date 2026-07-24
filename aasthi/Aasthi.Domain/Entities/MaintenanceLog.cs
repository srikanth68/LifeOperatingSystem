namespace Aasthi.Domain.Entities;

public class MaintenanceLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PropertyId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? VendorName { get; set; }
    public string? VendorContact { get; set; }
    public decimal? Cost { get; set; }
    public string Category { get; set; } = "repair";   // repair | improvement | inspection | other
    public DateOnly? CompletedDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Property Property { get; set; } = null!;
}
