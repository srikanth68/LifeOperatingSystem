namespace Vault.Worker.Models;

public class Account
{
    public string Id { get; set; } = string.Empty;
    public string PlaidAccountId { get; set; } = string.Empty;
    public string InstitutionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string SubType { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public decimal? AvailableBalance { get; set; }
    public string Currency { get; set; } = "USD";
    public bool IsActive { get; set; } = true;
    public string? RawPlaidData { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Institution? Institution { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
