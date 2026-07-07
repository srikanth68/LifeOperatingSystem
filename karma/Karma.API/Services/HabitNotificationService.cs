using Karma.Application.Interfaces;

namespace Karma.API.Services;

public class HabitNotificationService(IServiceProvider sp, ILogger<HabitNotificationService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await SendDueAsync(ct); }
            catch (Exception ex) { log.LogError(ex, "Habit notification tick failed"); }

            // align to next full minute
            var now = DateTime.Now;
            var delay = 60 - now.Second;
            await Task.Delay(TimeSpan.FromSeconds(delay), ct);
        }
    }

    private async Task SendDueAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKarmaRepository>();
        var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();
        if (!sender.IsConfigured) return;

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var currentTime = $"{now.Hour:D2}:{now.Minute:D2}";
        var dayOfWeek = (int)now.DayOfWeek;

        var habits = await repo.GetHabitsAsync(activeOnly: true);
        foreach (var habit in habits)
        {
            if (habit.NotifyTime != currentTime) continue;
            if (!habit.NotifyDays.Contains(dayOfWeek)) continue;
            if (habit.LastNotificationSentOn == today) continue;

            var msg = habit.NotifyMessage ?? $"{habit.Emoji} <b>{habit.Name}</b> — time to check in!";
            await sender.SendAsync(msg, habit.NotifyChannel, ct);
            await repo.MarkHabitNotifiedAsync(habit.Id, today);
            log.LogInformation("Sent habit notification: {Habit}", habit.Name);
        }
    }
}
