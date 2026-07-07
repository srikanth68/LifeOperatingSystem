using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using San.Application.Interfaces;

namespace San.Worker;

public class CalendarSyncWorker(IServiceProvider services, ILogger<CalendarSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Calendar sync worker started. Interval: {m}m", SyncInterval.TotalMinutes);

        using var timer = new PeriodicTimer(SyncInterval);
        do
        {
            await SyncAsync(ct);
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task SyncAsync(CancellationToken ct)
    {
        try
        {
            var google = services.GetRequiredService<IGoogleCalendarService>();

            if (!google.IsConfiguredAndAuthorized)
            {
                logger.LogDebug("Google Calendar not configured or not authorized — skipping sync.");
                return;
            }

            var count = await google.SyncEventsAsync(ct);
            logger.LogInformation("Calendar sync completed: {count} events synced.", count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Calendar sync failed");
        }
    }
}
