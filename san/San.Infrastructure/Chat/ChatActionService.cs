using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using San.Application.Interfaces;
using San.Domain.Entities;

namespace San.Infrastructure.Chat;

public partial class ChatActionService(ISanRepository repo, IModuleContextService moduleContext) : IChatActionService
{
    private static readonly string[] ValidAlertTypes = ["spending_threshold", "goal_deadline", "document_expiry", "custom"];

    public string ToolInstructions =>
        "=== ACTION TOOLS — READ CAREFULLY ===\n" +
        "You can take real actions: creating reminders, custom alerts, and calendar events. " +
        "This is NOT optional decoration — if the user asks you to remind/schedule/alert them " +
        "about something and you do not include the fenced block below in that SAME reply, " +
        "NOTHING gets created, no matter what you say in your sentence. Never say a reminder, " +
        "alert, or event was created unless you are including the block for it right now, in " +
        "this reply. If you are not taking an action, do not include the block — just answer " +
        "normally (e.g. for questions about what already exists, use the snapshot above).\n\n" +
        "Format: one short confirmation sentence, then on its own lines a fenced block starting " +
        "with ```action containing ONLY one JSON object — no comments, no other text inside it:\n\n" +
        "```action\n{\"tool\": \"create_reminder\", \"text\": \"Call the dentist\", \"dueAt\": \"2026-07-23T09:00:00\"}\n```\n\n" +
        "Worked example — user says \"remind me to go to the bank at 2:30pm today\", and your time " +
        "context above says today is 2026-07-23, you would reply exactly like this:\n" +
        "\"Done — I'll remind you to go to the bank at 2:30 PM today.\"\n" +
        "```action\n{\"tool\": \"create_reminder\", \"text\": \"Go to the bank\", \"dueAt\": \"2026-07-23T14:30:00\"}\n```\n\n" +
        "Available tools:\n" +
        "- create_reminder: {tool, text, dueAt}\n" +
        "- create_alert: {tool, type, title, description?, thresholdValue?, triggerAt?} — type is one of " +
        "spending_threshold|goal_deadline|document_expiry|custom. thresholdValue (a dollar number) is " +
        "required only for spending_threshold; triggerAt is required for the other three types.\n" +
        "- create_calendar_event: {tool, title, startTime, endTime, description?, location?}\n\n" +
        "IMPORTANT on dates: dueAt/triggerAt/startTime/endTime must be the user's LOCAL wall-clock time " +
        "(matching the timezone already stated in your time context above), formatted exactly as " +
        "yyyy-MM-ddTHH:mm:ss — no 'Z', no UTC offset. Use the EXACT current year/month/day already given " +
        "to you in your time context above as the anchor for any relative date (\"today\", \"tomorrow\") — " +
        "never guess or invent a year.\n\n" +
        "At most one action block per reply.";

    public async Task<string> BuildOwnContextAsync(CancellationToken ct = default)
    {
        var lines = new List<string>();
        var now = DateTime.UtcNow;

        var reminders = await repo.GetRemindersAsync();
        var pending = reminders.Where(r => !r.Done).OrderBy(r => r.DueAt).Take(5).ToList();
        if (pending.Count > 0)
            lines.Add("Upcoming reminders: " + string.Join("; ", pending.Select(r => $"\"{r.Text}\" (due {r.DueAt:u})")));

        var alerts = await repo.GetActiveAlertsAsync();
        if (alerts.Count > 0)
            lines.Add("Active alerts: " + string.Join("; ", alerts.Take(5).Select(a => $"\"{a.Title}\" ({a.Type})")));

        var events = await repo.GetCalendarEventsAsync(now, now.AddDays(7));
        if (events.Count > 0)
            lines.Add("Upcoming calendar events (7d): " + string.Join("; ", events.Take(5).Select(e => $"\"{e.Title}\" ({e.StartTime:u})")));

        if (lines.Count == 0) return "The user has no reminders, alerts, or upcoming calendar events right now.";
        return "San's own scheduled items:\n" + string.Join("\n", lines);
    }

    public async Task<string> ProcessAsync(string replyText, CancellationToken ct = default)
    {
        var match = ActionBlockRegex().Match(replyText);
        if (!match.Success) return replyText;

        var cleaned = ActionBlockRegex().Replace(replyText, "").TrimEnd();
        string? error;
        try
        {
            error = await ExecuteAsync(match.Groups[1].Value, ct);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        return error is null ? cleaned : $"{cleaned}\n\n⚠️ I tried to do that but hit an issue: {error}";
    }

    // Returns null on success, or a short human-readable error to surface to the user.
    private async Task<string?> ExecuteAsync(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tool", out var toolEl)) return "the action was missing a tool name.";
        var tool = toolEl.GetString();

        var tz = await moduleContext.ResolveTimeZoneAsync(ct);

        switch (tool)
        {
            case "create_reminder":
            {
                var text = GetString(root, "text");
                if (string.IsNullOrWhiteSpace(text)) return "the reminder was missing text.";
                if (!TryParseLocal(GetString(root, "dueAt"), tz, out var dueAt)) return "the reminder's due date/time wasn't understood.";
                await repo.AddReminderAsync(new Reminder { Text = text, DueAt = dueAt });
                return null;
            }
            case "create_alert":
            {
                var type = GetString(root, "type") ?? "custom";
                if (!ValidAlertTypes.Contains(type)) return $"'{type}' isn't a valid alert type.";
                var title = GetString(root, "title");
                if (string.IsNullOrWhiteSpace(title)) return "the alert was missing a title.";

                decimal? threshold = root.TryGetProperty("thresholdValue", out var tv) && tv.ValueKind is JsonValueKind.Number
                    ? tv.GetDecimal() : null;
                DateTime? triggerAt = null;
                if (type != "spending_threshold")
                {
                    if (!TryParseLocal(GetString(root, "triggerAt"), tz, out var t)) return "the alert's trigger date/time wasn't understood.";
                    triggerAt = t;
                }
                else if (threshold is null) return "a spending_threshold alert needs a dollar thresholdValue.";

                await repo.AddAlertAsync(new Alert
                {
                    Type = type, Title = title, Description = GetString(root, "description") ?? "",
                    ThresholdValue = threshold, TriggerAt = triggerAt,
                });
                return null;
            }
            case "create_calendar_event":
            {
                var title = GetString(root, "title");
                if (string.IsNullOrWhiteSpace(title)) return "the event was missing a title.";
                if (!TryParseLocal(GetString(root, "startTime"), tz, out var start)) return "the event's start time wasn't understood.";
                if (!TryParseLocal(GetString(root, "endTime"), tz, out var end)) return "the event's end time wasn't understood.";

                await repo.UpsertCalendarEventAsync(new CalendarEvent
                {
                    Title = title, Description = GetString(root, "description"),
                    StartTime = start, EndTime = end, Location = GetString(root, "location"),
                    Source = "san-chat",
                });
                return null;
            }
            default:
                return $"'{tool}' isn't a tool San knows about.";
        }
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // Parses a naive "yyyy-MM-ddTHH:mm:ss"-style local wall-clock string and converts it to
    // UTC using the resolved timezone — mirrors the web frontend's localInputToUtcIso, just
    // done server-side since the model (not a browser) is producing the local time string.
    private static bool TryParseLocal(string? raw, TimeZoneInfo tz, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var trimmed = raw.Trim().TrimEnd('Z');
        if (!DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
        utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), tz);
        return true;
    }

    [GeneratedRegex(@"```action\s*\n(.*?)\n```", RegexOptions.Singleline)]
    private static partial Regex ActionBlockRegex();
}
