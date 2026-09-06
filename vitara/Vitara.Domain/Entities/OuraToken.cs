namespace Vitara.Domain.Entities;

public class OuraToken
{
    public int Id { get; set; }
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime LinkedAt { get; set; }

    // When data was last actually written.
    //
    // Previously stamped at the end of every sync attempt, including ones where every
    // single collection failed and nothing was persisted -- so the UI could show
    // "synced today" while the ring data was a week stale. It now moves only when at
    // least one collection came back with rows.
    public DateTime? LastSyncedAt { get; set; }

    // When a sync was last ATTEMPTED, whatever the outcome. The pair is the point: an
    // attempt far newer than a success means the sync is running and failing, which is
    // a completely different problem from the sync not running at all, and the old
    // single field could not tell them apart.
    public DateTime? LastSyncAttemptAt { get; set; }

    // Why the last attempt did not fully succeed, or null when it did. Kept because
    // failure here is otherwise invisible: Vitara has no notifier, so without this the
    // only trace is a container log line nobody reads.
    public string? LastSyncError { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    // Data is considered stale after this long without a successful sync. The sync runs
    // daily, so two days allows one missed run before anything is said -- a single
    // hiccup is not worth reporting, two in a row is.
    public bool IsStale => LastSyncedAt is null || DateTime.UtcNow - LastSyncedAt.Value > TimeSpan.FromDays(2);
}
