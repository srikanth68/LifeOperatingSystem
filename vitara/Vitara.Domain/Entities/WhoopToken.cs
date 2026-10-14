namespace Vitara.Domain.Entities;

// A linked WHOOP account (Vitara ADR 0029). The same shape and the same honesty fields as
// OuraToken, kept as its own table so a person can link either device, and linking one never
// touches the other.
public class WhoopToken
{
    public int Id { get; set; }
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime LinkedAt { get; set; }

    // When data was last written, when a sync was last tried, and why the last try fell
    // short. See OuraToken: the pair is what separates "not running" from "running and broken".
    public DateTime? LastSyncedAt { get; set; }
    public DateTime? LastSyncAttemptAt { get; set; }
    public string? LastSyncError { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsStale => LastSyncedAt is null || DateTime.UtcNow - LastSyncedAt.Value > TimeSpan.FromDays(2);
}
