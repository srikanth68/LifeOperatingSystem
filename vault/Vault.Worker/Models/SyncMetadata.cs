namespace Vault.Worker.Models;

public class SyncMetadata
{
    public string Id { get; set; } = string.Empty;
    public DateTime LastSyncTime { get; set; }
    public DateTime? NextScheduledSyncTime { get; set; }
    public string Status { get; set; } = "pending"; // pending, in_progress, completed, failed
    public int? TransactionsAdded { get; set; }
    public int? TransactionsUpdated { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
