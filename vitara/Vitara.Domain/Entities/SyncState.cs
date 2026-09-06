namespace Vitara.Domain.Entities;

// The health of one data source that is not Oura.
//
// Oura carries its sync state on the token, because it has one. Everything else needs
// somewhere to record the same three facts, and they are the three that matter: when
// data last actually arrived, when arrival was last attempted, and what went wrong.
//
// The pair of timestamps is the point. One field could never distinguish a source that
// is not running from one that is running and failing, and those need completely
// different responses.
public class SyncState
{
    public string Source { get; set; } = "";          // "mfp", and whatever comes next

    // Moves only when rows were actually written, never on a bare attempt.
    public DateTime? LastSyncedAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }

    // A scraper against a site that changes underneath it will break, so the question
    // is only whether anyone finds out. Two days allows one missed daily run.
    public bool IsStale => LastSyncedAt is null || DateTime.UtcNow - LastSyncedAt.Value > TimeSpan.FromDays(2);
}
