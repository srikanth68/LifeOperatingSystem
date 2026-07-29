namespace San.Domain.Entities;

public class EmailAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = "";       // "google" | "microsoft"
    public string EmailAddress { get; set; } = "";
    public string TokenJson { get; set; } = "";       // provider-specific serialized token set
    public bool Active { get; set; } = true;
    public DateTime? LastCheckedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
