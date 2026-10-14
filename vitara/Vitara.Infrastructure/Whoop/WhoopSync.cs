using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;

namespace Vitara.Infrastructure.Whoop;

// One WHOOP sync for one person, used by both the "Sync now" button and the worker, so the
// two can never disagree about what a sync does or what it records when it fails.
public static class WhoopSync
{
    public record Result(bool Ok, int Sleep, int Readiness, int Workouts, string? Error);

    public static async Task<Result> RunAsync(IVitaraRepository repo, IWhoopClient client, WhoopToken token, int days)
    {
        token.LastSyncAttemptAt = DateTime.UtcNow;
        try
        {
            // Access tokens last about an hour, so a nightly sync always refreshes. WHOOP
            // rotates the refresh token too; the new one replaces the old in the same save.
            if (token.ExpiresAt - DateTime.UtcNow < TimeSpan.FromMinutes(5))
            {
                var fresh = await client.RefreshAsync(token.RefreshToken);
                token.AccessToken = fresh.AccessToken;
                if (!string.IsNullOrEmpty(fresh.RefreshToken)) token.RefreshToken = fresh.RefreshToken;
                token.ExpiresAt = DateTime.UtcNow.AddSeconds(fresh.ExpiresIn);
            }

            var to = DateTimeOffset.UtcNow;
            var bundle = await client.FetchAsync(token.AccessToken, to.AddDays(-days), to);

            await repo.UpsertSleepAsync(bundle.Sleep);
            await repo.UpsertReadinessAsync(bundle.Readiness);
            await repo.UpsertWorkoutsAsync(bundle.Workouts);

            // Moves only on a real write, like Oura's: "synced" must mean data arrived.
            if (bundle.Sleep.Count + bundle.Readiness.Count + bundle.Workouts.Count > 0)
                token.LastSyncedAt = DateTime.UtcNow;
            token.LastSyncError = null;
            await repo.SaveWhoopTokenAsync(token);
            return new Result(true, bundle.Sleep.Count, bundle.Readiness.Count, bundle.Workouts.Count, null);
        }
        catch (Exception ex)
        {
            token.LastSyncError = ex.Message;
            await repo.SaveWhoopTokenAsync(token);
            return new Result(false, 0, 0, 0, ex.Message);
        }
    }
}
