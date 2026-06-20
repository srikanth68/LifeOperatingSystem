namespace Aasthi.Domain.Entities;

public class Property
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Zip { get; set; } = "";
    public string Country { get; set; } = "USA";

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public decimal PurchasePrice { get; set; }
    public DateOnly? PurchaseDate { get; set; }

    public decimal CurrentValue { get; set; }
    public DateOnly? CurrentValueAsOf { get; set; }

    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<PropertyContact> Contacts { get; set; } = [];
    public List<PropertyDocument> Documents { get; set; } = [];

    public decimal ProfitAmount => CurrentValue - PurchasePrice;
    public double? ProfitPct => PurchasePrice > 0
        ? (double)((CurrentValue - PurchasePrice) / PurchasePrice * 100)
        : null;
}
