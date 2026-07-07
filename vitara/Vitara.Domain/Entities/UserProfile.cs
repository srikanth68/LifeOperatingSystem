namespace Vitara.Domain.Entities;

public class UserProfile
{
    public string Id { get; set; } = "default";
    public int? Age { get; set; }
    public double? Weight { get; set; }      // kg
    public double? Height { get; set; }      // m
    public string? BiologicalSex { get; set; }
    public string? Email { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
