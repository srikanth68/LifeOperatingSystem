using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using San.Application;
using San.Application.Interfaces;

namespace San.Worker;

// One message each morning with the shape of the day.
//
// The agenda answers "what's on" when the user thinks to ask. This is the half that
// makes San feel like it is holding their life rather than waiting to be queried —
// the same ranking, delivered without being asked for.
//
// Deliberately NOT routed through FindingDispatcher, unlike every other notification
// in the system. That path exists to stop findings repeating, and repetition is the
// entire point here: today's brief will mention the same overdue task as yesterday's,
// and a cooldown would read that as spam and silence the feature. A scheduled digest
// and an unprompted finding are different kinds of message and want opposite rules.
public class MorningBriefWorker(IServiceProvider services, ILogger<MorningBriefWorker> logger) : BackgroundService
{
    // Checked often, sent once — the send is gated on the day, not the tick.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);

    private const string LastSentKey = "brief.last_sent_day";

    // After the quiet-hours release at 07:00, so the held-overnight message and the
    // brief don't arrive together — and late enough to be worth reading.
    private static int SendHour =>
        int.TryParse(Environment.GetEnvironmentVariable("MORNING_BRIEF_HOUR"), out var h) && h is >= 0 and <= 23
            ? h : 7;

    private static int SendMinute =>
        int.TryParse(Environment.GetEnvironmentVariable("MORNING_BRIEF_MINUTE"), out var m) && m is >= 0 and <= 59
            ? m : 30;

    // A brief that sometimes doesn't arrive is indistinguishable from a broken worker,
    // which is the exact failure the self-check exists to prevent. So an empty day
    // still gets a line, unless the user turns that off.
    private static bool SendWhenEmpty =>
        !string.Equals(Environment.GetEnvironmentVariable("MORNING_BRIEF_WHEN_EMPTY"), "false",
            StringComparison.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Morning brief worker started. Sends at {H:D2}:{M:D2} local.", SendHour, SendMinute);

        using var timer = new PeriodicTimer(CheckInterval);
        do
        {
            await RunAsync(ct);
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        string? error = null;
        try
        {
            using var scope = services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISanRepository>();
            var telegram = scope.ServiceProvider.GetRequiredService<ITelegramNotifier>();
            var moduleContext = scope.ServiceProvider.GetRequiredService<IModuleContextService>();
            var agenda = scope.ServiceProvider.GetRequiredService<IAgendaService>();

            if (!telegram.IsConfigured) return;

            var tz = await moduleContext.ResolveTimeZoneAsync(ct);
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var today = DateOnly.FromDateTime(nowLocal).ToString("yyyy-MM-dd");

            // Not yet time today.
            if (nowLocal.Hour < SendHour || (nowLocal.Hour == SendHour && nowLocal.Minute < SendMinute)) return;

            // Already sent today. Without this a ten-minute timer would send a brief
            // every ten minutes from 07:30 until midnight.
            if (await repo.GetSettingAsync(LastSentKey) == today) return;

            var items = await agenda.BuildAsync(20, ct);

            if (items.Count == 0 && !SendWhenEmpty)
            {
                // Still marked as sent: the decision for today has been made, and
                // re-evaluating every ten minutes would eventually find something and
                // send a "morning" brief in the afternoon.
                await repo.SetSettingAsync(LastSentKey, today);
                logger.LogInformation("Morning brief: nothing on today, staying quiet.");
                return;
            }

            await telegram.SendAsync(Compose(items, nowLocal), ct);
            await repo.SetSettingAsync(LastSentKey, today);
            logger.LogInformation("Morning brief sent with {Count} item(s).", items.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Morning brief failed");
            error = ex.Message;
        }
        finally
        {
            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IHealthTracker>()
                    .RecordAsync(HealthComponents.WorkerMorningBrief, error is null, error, ct);
            }
            catch { /* bookkeeping must never be why a run counts as failed */ }
        }
    }

    private static string Compose(IReadOnlyList<AgendaItem> items, DateTime nowLocal)
    {
        var header = $"☀️ {nowLocal:dddd, d MMMM}";
        if (items.Count == 0) return $"{header}\n\nNothing scheduled — clear day.";

        var lines = new List<string> { header, "" };

        // Grouped, because a flat list of twenty things is a wall. Order matches the
        // agenda's own ranking, so the groups appear worst-first.
        foreach (var (bucket, label) in new[]
        {
            ("overdue", "Overdue"), ("now", "Right now"), ("soon", "Soon"),
            ("today", "Today"), ("tomorrow", "Tomorrow"), ("open", "Still open"),
        })
        {
            var group = items.Where(i => i.Bucket == bucket).ToList();
            if (group.Count == 0) continue;

            lines.Add($"<b>{label}</b>");
            foreach (var i in group)
            {
                var time = i.WhenLocal is { } w ? $"{w:h:mm tt}  " : "";
                var detail = string.IsNullOrWhiteSpace(i.Detail) ? "" : $" — {i.Detail}";
                lines.Add($"{Icon(i.Kind)} {time}{i.Title}{detail}");
            }
            lines.Add("");
        }

        return string.Join("\n", lines).TrimEnd();
    }

    private static string Icon(string kind) => kind switch
    {
        "event" => "📅",
        "reminder" => "⏰",
        "alert" => "🔔",
        "task" => "🔧",
        "action" => "📌",
        "habit" => "⚪",
        _ => "•",
    };
}
