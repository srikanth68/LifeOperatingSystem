using Vault.Worker.Services;

namespace Vault.Worker.Jobs;

public class ScheduledSyncWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScheduledSyncWorker> _logger;
    private readonly TimeSpan _syncTime;

    public ScheduledSyncWorker(IServiceProvider serviceProvider, ILogger<ScheduledSyncWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        // Parse sync time from environment or use default (2 AM)
        var syncTimeStr = Environment.GetEnvironmentVariable("SYNC_TIME") ?? "02:00";
        var parts = syncTimeStr.Split(':');
        _syncTime = new TimeSpan(int.Parse(parts[0]), int.Parse(parts[1]), 0);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"Scheduled sync worker started. Sync time set to {_syncTime:hh\\:mm}");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRunTime = GetNextRunTime(now);
            var timeUntilNextRun = nextRunTime - now;

            _logger.LogInformation($"Next sync scheduled for {nextRunTime:yyyy-MM-dd HH:mm:ss} (in {timeUntilNextRun.TotalHours:F1} hours)");

            try
            {
                await Task.Delay(timeUntilNextRun, stoppingToken);

                // Execute sync
                using (var scope = _serviceProvider.CreateScope())
                {
                    var syncService = scope.ServiceProvider.GetRequiredService<ISyncService>();
                    _logger.LogInformation("Executing scheduled sync...");
                    var result = await syncService.SyncAsync();
                    _logger.LogInformation($"Scheduled sync completed with status: {result.Status}");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Scheduled sync worker is stopping");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled sync");
            }
        }
    }

    private DateTime GetNextRunTime(DateTime now)
    {
        var nextRun = now.Date.Add(_syncTime);

        // If the sync time has already passed today, schedule for tomorrow
        if (nextRun <= now)
        {
            nextRun = nextRun.AddDays(1);
        }

        return nextRun;
    }
}
