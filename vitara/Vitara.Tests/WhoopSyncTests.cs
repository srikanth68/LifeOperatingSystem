using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;
using Vitara.Infrastructure.Whoop;

namespace Vitara.Tests;

// The one sync both "Sync now" and the worker run (ADR 0029).
public class WhoopSyncTests
{
    private sealed class FakeWhoop(WhoopBundle bundle, bool fail = false) : IWhoopClient
    {
        public int Refreshes;
        public string AuthorizeUrl(string state) => "https://whoop.test/?state=" + state;
        public Task<WhoopTokens> ExchangeCodeAsync(string code) => Task.FromResult(new WhoopTokens("a", "r", 3600));
        public Task<WhoopTokens> RefreshAsync(string refreshToken) { Refreshes++; return Task.FromResult(new WhoopTokens("a2", "r2", 3600)); }
        public Task<WhoopBundle> FetchAsync(string accessToken, DateTimeOffset from, DateTimeOffset to) =>
            fail ? throw new InvalidOperationException("WHOOP /recovery failed: HTTP 401") : Task.FromResult(bundle);
    }

    private static WhoopBundle OneNight() => new(
        [new SleepSession { Id = "whoop-s1", Day = new DateOnly(2026, 10, 12), TotalSleepMinutes = 420 }],
        [new DailyReadiness { Id = "whoop-2026-10-12", Day = new DateOnly(2026, 10, 12), Score = 72 }], []);

    [Fact]
    public async Task AnExpiredTokenIsRefreshedAndRotated_ThenTheDataIsWritten()
    {
        var repo = new FakeRepo();
        var whoop = new FakeWhoop(OneNight());
        var token = new WhoopToken { AccessToken = "old", RefreshToken = "r1", ExpiresAt = DateTime.UtcNow.AddMinutes(-1) };

        var r = await WhoopSync.RunAsync(repo, whoop, token, 7);

        Assert.True(r.Ok);
        Assert.Equal(1, whoop.Refreshes);
        Assert.Equal("a2", repo.WhoopTokenData!.AccessToken);
        Assert.Equal("r2", repo.WhoopTokenData.RefreshToken);
        Assert.Single(repo.SleepData);
        Assert.Single(repo.ReadinessData);
        Assert.NotNull(repo.WhoopTokenData.LastSyncedAt);
        Assert.Null(repo.WhoopTokenData.LastSyncError);
    }

    [Fact]
    public async Task AFailureIsRecordedOnTheToken_AndSyncedIsNotMoved()
    {
        var repo = new FakeRepo();
        var token = new WhoopToken { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddHours(1) };

        var r = await WhoopSync.RunAsync(repo, new FakeWhoop(OneNight(), fail: true), token, 7);

        Assert.False(r.Ok);
        Assert.Contains("401", repo.WhoopTokenData!.LastSyncError);
        Assert.Null(repo.WhoopTokenData.LastSyncedAt);
        Assert.NotNull(repo.WhoopTokenData.LastSyncAttemptAt);
    }
}
