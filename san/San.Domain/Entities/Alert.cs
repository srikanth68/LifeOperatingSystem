namespace San.Domain.Entities;

// Alert types:
//  - "spending_threshold": fires when Vault's trailing-30-day spend exceeds ThresholdValue.
//      Re-arms automatically once spend drops back below threshold (TriggeredAt cleared).
//  - "custom" / "goal_deadline" / "document_expiry": one-shot, fires once at TriggerAt.
public class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "custom";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    // For spending_threshold
    public decimal? ThresholdValue { get; set; }

    // For one-shot time-based alerts (goal_deadline, document_expiry, custom)
    public DateTime? TriggerAt { get; set; }

    public bool Active { get; set; } = true;
    public bool NotifyTelegram { get; set; } = true;
    public DateTime? TriggeredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
