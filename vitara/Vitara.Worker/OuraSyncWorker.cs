using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vitara.Application.Interfaces;
using Vitara.Infrastructure.Data;

namespace Vitara.Worker;

public class OuraSyncWorker(IServiceProvider services, ILogger<OuraSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Vitara sync worker started. Interval: {h}h", SyncInterval.TotalHours);

        // Initial sync on startup
        await SyncAsync(ct);

        using var timer = new PeriodicTimer(SyncInterval);
        while (await timer.WaitForNextTickAsync(ct))
            await SyncAsync(ct);
    }

    private async Task SyncAsync(CancellationToken ct)
    {
        logger.LogInformation("Starting Oura sync at {time}", DateTimeOffset.UtcNow);
        try
        {
            using var scope = services.CreateScope();
            var repo   = scope.ServiceProvider.GetRequiredService<IVitaraRepository>();
            var client = scope.ServiceProvider.GetRequiredService<IOuraClient>();

            var token = await repo.GetTokenAsync();
            if (token is null) { logger.LogWarning("No Oura token — skipping sync. Link at http://localhost:5100/api/oura/auth"); return; }

            // Refresh token if expiring within 1 hour
            if (token.ExpiresAt - DateTime.UtcNow < TimeSpan.FromHours(1))
            {
                logger.LogInformation("Token expiring soon, refreshing...");
                var refreshed = await client.RefreshAccessTokenAsync(token.RefreshToken);
                var payload   = JsonSerializer.Deserialize<TokenPayload>(refreshed, _json)!;
                token.AccessToken  = payload.AccessToken;
                token.RefreshToken = payload.RefreshToken;
                token.ExpiresAt    = DateTime.UtcNow.AddSeconds(payload.ExpiresIn);
                await repo.SaveTokenAsync(token);
            }

            var to   = DateOnly.FromDateTime(DateTime.UtcNow);
            var from = to.AddDays(-30);

            var sleep     = await client.GetSleepAsync(token.AccessToken, from, to);
            var readiness = await client.GetReadinessAsync(token.AccessToken, from, to);
            var activity  = await client.GetActivityAsync(token.AccessToken, from, to);

            await repo.UpsertSleepAsync(sleep);
            await repo.UpsertReadinessAsync(readiness);
            await repo.UpsertActivityAsync(activity);

            logger.LogInformation("Sync complete — sleep:{s} readiness:{r} activity:{a}", sleep.Count, readiness.Count, activity.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sync failed");
        }
    }

    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private record TokenPayload(
        [property: JsonPropertyName("access_token")]  string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")]    int ExpiresIn
    );
}
