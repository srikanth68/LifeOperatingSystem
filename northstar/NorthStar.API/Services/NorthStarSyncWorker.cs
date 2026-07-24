namespace NorthStar.API.Services;

// Runs the same sync the "Sync All Modules" button triggers, automatically,
// every 15 minutes — so Recent Knowledge / module health stay fresh without
// anyone having to remember to click it.
public class NorthStarSyncWorker(IServiceScopeFactory scopeFactory, ILogger<NorthStarSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting before the first run.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<ModuleSyncService>();
                var results = await syncService.SyncAllAsync();
                logger.LogInformation("Auto-sync: {Results}", string.Join(", ", results.Select(r => $"{r.Key}={r.Value}")));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-sync failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
