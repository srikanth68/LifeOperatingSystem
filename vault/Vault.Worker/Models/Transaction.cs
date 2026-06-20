namespace Vault.Worker.Models;

public class Transaction
{
    public string Id { get; set; } = string.Empty;
    public string PlaidTransactionId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime TransactionDate { get; set; }
    public DateTime? AuthorizedDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? MerchantName { get; set; }
    public string? Category { get; set; }
    public string? SubCategory { get; set; }
    public bool IsPending { get; set; }
    public string? RawPlaidData { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Account? Account { get; set; }
}
