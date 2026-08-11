using Karma.Application;
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

        var habits = await repo.GetHabitsAsync(activeOnly: true);
        foreach (var habit in habits)
        {
            if (!HabitSchedule.IsDue(habit, now)) continue;

            var msg = habit.NotifyMessage ?? $"{habit.Emoji} <b>{habit.Name}</b> — time to check in!";
            await sender.SendAsync(msg, habit.NotifyChannel, ct);
            // Marked BEFORE anything else can throw on the next iteration, and marked
            // even though the send already succeeded, so a failure further down the
            // list can never resend this one.
            await repo.MarkHabitNotifiedAsync(habit.Id, today);

            var late = HabitSchedule.TryParseNotifyTime(habit.NotifyTime, out var at)
                ? now - now.Date.Add(at) : TimeSpan.Zero;
            if (late > TimeSpan.FromMinutes(1))
                log.LogWarning("Sent habit notification {Habit} {Minutes:F0}m late — recovered a reminder that would previously have been lost for the day.",
                    habit.Name, late.TotalMinutes);
            else
                log.LogInformation("Sent habit notification: {Habit}", habit.Name);
        }
    }
}
