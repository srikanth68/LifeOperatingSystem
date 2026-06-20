using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using San.Application.Interfaces;

namespace San.Worker;

// Checks due reminders and active alerts on a short interval and pushes Telegram notifications.
// - Reminders: one-shot, fires once DueAt has passed (NotifiedAt gets set so it won't repeat).
// - Time-based alerts (goal_deadline / document_expiry / custom): same one-shot pattern via TriggerAt.
// - spending_threshold alerts: polls Vault's trailing-30-day spend; fires when it crosses the
//   threshold and re-arms automatically once spend drops back below it.
public class NotificationWorker(IServiceProvider services, ILogger<NotificationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("San notification worker started. Interval: {m}m", CheckInterval.TotalMinutes);

        using var timer = new PeriodicTimer(CheckInterval);
        do
        {
            await CheckAsync(ct);
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        try
        {
            using var scope = services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISanRepository>();
            var telegram = scope.ServiceProvider.GetRequiredService<ITelegramNotifier>();
            var moduleContext = scope.ServiceProvider.GetRequiredService<IModuleContextService>();

            if (!telegram.IsConfigured)
            {
                logger.LogDebug("Telegram not configured — skipping notification check.");
                return;
            }

            var now = DateTime.UtcNow;

            var dueReminders = await repo.GetDueUnnotifiedRemindersAsync(now);
            foreach (var r in dueReminders)
            {
                await telegram.SendAsync($"⏰ Reminder: {r.Text}", ct);
                await repo.UpdateReminderAsync(r.Id, x => x.NotifiedAt = now);
                logger.LogInformation("Sent reminder notification: {text}", r.Text);
            }

            var activeAlerts = await repo.GetActiveAlertsAsync();
            decimal? trailingSpend = null;

            foreach (var a in activeAlerts)
            {
                if (!a.NotifyTelegram) continue;

                if (a.Type == "spending_threshold" && a.ThresholdValue is { } threshold)
                {
                    trailingSpend ??= await moduleContext.GetTrailing30DaySpendAsync(ct);
                    if (trailingSpend is null) continue;

                    var breached = trailingSpend.Value >= threshold;
                    if (breached && a.TriggeredAt is null)
                    {
                        await telegram.SendAsync($"⚠️ {a.Title}: 30-day spend ${trailingSpend:N0} crossed your ${threshold:N0} threshold.", ct);
                        await repo.UpdateAlertAsync(a.Id, x => x.TriggeredAt = now);
                        logger.LogInformation("Sent spending_threshold alert: {title}", a.Title);
                    }
                    else if (!breached && a.TriggeredAt is not null)
                    {
                        // Re-arm once spend drops back below the threshold.
                        await repo.UpdateAlertAsync(a.Id, x => x.TriggeredAt = null);
                    }
                }
                else if (a.TriggerAt is { } triggerAt && triggerAt <= now && a.TriggeredAt is null)
                {
                    await telegram.SendAsync($"⚠️ {a.Title}: {a.Description}", ct);
                    await repo.UpdateAlertAsync(a.Id, x => x.TriggeredAt = now);
                    logger.LogInformation("Sent time-based alert: {title}", a.Title);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Notification check failed");
        }
    }
}
