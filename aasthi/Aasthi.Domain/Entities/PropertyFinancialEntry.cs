namespace Aasthi.Domain.Entities;

public class PropertyFinancialEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PropertyId { get; set; }
    public string Type { get; set; } = "expense";      // income | expense | mortgage
    public string Category { get; set; } = "other";    // rent | tax | insurance | repair | mortgage_payment | hoa | utility | other
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Property Property { get; set; } = null!;
}
