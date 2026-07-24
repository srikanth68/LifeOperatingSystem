namespace Vitara.Domain.Entities;

public class WeighIn
{
    public string Id { get; set; } = "";   // Day string (yyyy-MM-dd) — one weigh-in per day, upsert semantics
    public DateOnly Day { get; set; }
    public double WeightKg { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
