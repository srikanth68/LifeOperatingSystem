using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Maaya.Mcp.Tools;

// Write/action tools — the "hands" of any agent harness (San's own agent loop, Claude, custom)
// driving Maaya. Without these the gateway is read-only, and an agent asked to
// "remind me at 4pm" correctly concludes Maaya can't do it and either falls back to
// something else (Apple Reminders) or dumps it in NorthStar's action queue.
// Descriptions deliberately steer the agent to use THESE for the user's own data.
//
// Datetimes are ISO-8601 UTC (e.g. 2026-07-23T20:30:00Z) — resolve the user's local
// wall-clock time to UTC first; their timezone lives in the brain (facts_list, key
// "timezone"). Plain dates are yyyy-MM-dd in the user's local day.
//
// Deliberately NOT exposed (do these by hand in the dashboard): creating properties,
// budgets, uploading documents — higher-stakes/structured entry the user owns.
[McpServerToolType]
public sealed class ActionTools(ModuleGateway gw)
{
    // ── Time handling ───────────────────────────────────────────────────────────
    // Agents are bad at timezone arithmetic. Making the model compute UTC produced
    // reminders stamped in the past, which the notification worker then fired the
    // instant they were created. So: accept the user's LOCAL wall-clock time (what
    // the model can reliably transcribe) and do the conversion here, server-side.
    // An explicit UTC/offset timestamp is still accepted and passed through.

    private async Task<TimeZoneInfo> ResolveTimeZoneAsync()
    {
        try
        {
            var json = await gw.GetAsync("northstar", "/api/facts/timezone");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("value", out var v)
                && v.GetString() is { Length: > 0 } id)
                return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch { /* fact unset or NorthStar down — fall back to the container's TZ */ }
        return TimeZoneInfo.Local;
    }

    private static bool LooksAbsolute(string s)
    {
        var t = s.IndexOf('T');
        if (t < 0) return s.EndsWith("Z", StringComparison.OrdinalIgnoreCase);
        var time = s[(t + 1)..];
        return time.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
               || time.Contains('+') || time.Contains('-');
    }

    // Returns (utcIso, error). Exactly one is non-null.
    private async Task<(string? Utc, string? Error)> ToUtcAsync(string input, string field, bool rejectPast)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (null, $"{field} is required.");

        var s = input.Trim();
        DateTime utc;

        if (LooksAbsolute(s) && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            utc = dto.UtcDateTime;
        }
        else if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var naive))
        {
            var tz = await ResolveTimeZoneAsync();
            utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(naive, DateTimeKind.Unspecified), tz);
        }
        else
        {
            return (null, $"Couldn't parse {field} '{input}'. Use the user's LOCAL wall-clock time as yyyy-MM-ddTHH:mm:ss.");
        }

        if (rejectPast && utc < DateTime.UtcNow.AddMinutes(-2))
        {
            var tz = await ResolveTimeZoneAsync();
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            return (null,
                $"{field} resolves to {local:yyyy-MM-dd HH:mm} local, which is in the PAST — it would fire immediately. " +
                $"It is currently {nowLocal:yyyy-MM-dd HH:mm} local. Re-send the correct future date/time.");
        }

        return (utc.ToString("yyyy-MM-ddTHH:mm:ssZ"), null);
    }

    private static string Fail(string message) =>
        JsonSerializer.Serialize(new { ok = false, error = message });

    // ── San: reminders ──

    [McpServerTool(Name = "reminder_create")]
    [Description("Create a reminder in the user's own Maaya/San system. USE THIS for any 'remind me…' request — do NOT use Apple Reminders or any other reminder app, and do NOT use action_add (that's a passive backlog, it never notifies).")]
    public async Task<string> ReminderCreate(
        [Description("What to remind the user about, e.g. 'Go to the bank'.")] string text,
        [Description("When to fire, in the user's LOCAL wall-clock time as yyyy-MM-ddTHH:mm:ss (e.g. 2026-07-23T22:00:00 for 10pm tonight). Do NOT convert to UTC — just write the time the user said, with the correct date. The server converts it.")] string dueAt,
        [Description("Also send a Telegram push at the due time (default true).")] bool notifyTelegram = true)
    {
        var (utc, error) = await ToUtcAsync(dueAt, "dueAt", rejectPast: true);
        if (error is not null) return Fail(error);
        return await gw.SendAsync("san", HttpMethod.Post, "/api/reminders",
            new { text, dueAt = utc, notifyTelegram });
    }

    [McpServerTool(Name = "reminders_list")]
    [Description("List the user's Maaya/San reminders — answers 'what are my reminders', and gives you the id needed to update/complete/delete one.")]
    public Task<string> RemindersList() => gw.GetAsync("san", "/api/reminders");

    [McpServerTool(Name = "reminder_update")]
    [Description("Edit an existing reminder's text or due time. Get the id from reminders_list. Editing re-arms its notification.")]
    public async Task<string> ReminderUpdate(
        [Description("Reminder GUID from reminders_list.")] string reminderId,
        [Description("New reminder text.")] string text,
        [Description("New due time, in the user's LOCAL wall-clock time as yyyy-MM-ddTHH:mm:ss. Do NOT convert to UTC.")] string dueAt,
        [Description("Send a Telegram push at the due time.")] bool notifyTelegram = true)
    {
        var (utc, error) = await ToUtcAsync(dueAt, "dueAt", rejectPast: true);
        if (error is not null) return Fail(error);
        return await gw.SendAsync("san", HttpMethod.Put, $"/api/reminders/{reminderId}",
            new { text, dueAt = utc, notifyTelegram });
    }

    [McpServerTool(Name = "reminder_complete")]
    [Description("Tick a reminder off (or un-tick it). Get the id from reminders_list.")]
    public Task<string> ReminderComplete(
        [Description("Reminder GUID.")] string reminderId,
        [Description("True = done, false = reopen.")] bool done = true) =>
        gw.SendAsync("san", HttpMethod.Patch, $"/api/reminders/{reminderId}/done", done);

    [McpServerTool(Name = "reminder_delete")]
    [Description("Permanently delete a reminder. Prefer reminder_complete unless the user explicitly wants it removed.")]
    public Task<string> ReminderDelete(
        [Description("Reminder GUID.")] string reminderId) =>
        gw.SendAsync("san", HttpMethod.Delete, $"/api/reminders/{reminderId}", null);

    // ── San: alerts ──

    [McpServerTool(Name = "alert_create")]
    [Description("Create an alert in Maaya/San — e.g. a document-expiry or goal-deadline warning. For time-based types (goal_deadline, document_expiry, custom) give triggerAt (ISO-8601 UTC). For spending_threshold give thresholdValue (dollars) instead.")]
    public async Task<string> AlertCreate(
        [Description("One of: spending_threshold, goal_deadline, document_expiry, custom.")] string type,
        [Description("Short alert title.")] string title,
        [Description("Optional longer description.")] string? description = null,
        [Description("Dollar amount — REQUIRED for spending_threshold, omit otherwise.")] decimal? thresholdValue = null,
        [Description("Fire time in the user's LOCAL wall-clock time as yyyy-MM-ddTHH:mm:ss (do NOT convert to UTC) — REQUIRED for time-based types.")] string? triggerAt = null,
        [Description("Also send a Telegram push (default true).")] bool notifyTelegram = true)
    {
        string? utc = null;
        if (!string.IsNullOrWhiteSpace(triggerAt))
        {
            var (converted, error) = await ToUtcAsync(triggerAt, "triggerAt", rejectPast: true);
            if (error is not null) return Fail(error);
            utc = converted;
        }
        return await gw.SendAsync("san", HttpMethod.Post, "/api/alerts",
            new { type, title, description = description ?? "", thresholdValue, triggerAt = utc, active = true, notifyTelegram });
    }

    [McpServerTool(Name = "alerts_list")]
    [Description("List the user's Maaya/San alerts, with ids for updating or deleting.")]
    public Task<string> AlertsList() => gw.GetAsync("san", "/api/alerts");

    [McpServerTool(Name = "alert_delete")]
    [Description("Delete an alert. Get the id from alerts_list.")]
    public Task<string> AlertDelete(
        [Description("Alert GUID.")] string alertId) =>
        gw.SendAsync("san", HttpMethod.Delete, $"/api/alerts/{alertId}", null);

    // ── San: calendar ──

    [McpServerTool(Name = "calendar_event_create")]
    [Description("Create an event on the user's Maaya/San calendar. USE THIS for 'schedule…' / 'put it on my calendar' rather than any external calendar app. startTime/endTime MUST be ISO-8601 UTC.")]
    public async Task<string> CalendarEventCreate(
        [Description("Event title.")] string title,
        [Description("Start time in the user's LOCAL wall-clock time as yyyy-MM-ddTHH:mm:ss. Do NOT convert to UTC.")] string startTime,
        [Description("End time in the user's LOCAL wall-clock time as yyyy-MM-ddTHH:mm:ss. Do NOT convert to UTC.")] string endTime,
        [Description("Optional description.")] string? description = null,
        [Description("Optional location.")] string? location = null)
    {
        // Past events are legitimate here (logging something that already happened).
        var (startUtc, startErr) = await ToUtcAsync(startTime, "startTime", rejectPast: false);
        if (startErr is not null) return Fail(startErr);
        var (endUtc, endErr) = await ToUtcAsync(endTime, "endTime", rejectPast: false);
        if (endErr is not null) return Fail(endErr);

        return await gw.SendAsync("san", HttpMethod.Post, "/api/calendar/events",
            new { title, description, startTime = startUtc, endTime = endUtc, location, allDay = false });
    }

    [McpServerTool(Name = "calendar_events_list")]
    [Description("List the user's upcoming Maaya/San calendar events in a date window.")]
    public Task<string> CalendarEventsList(
        [Description("Window start, ISO-8601 UTC. Omit for today.")] string? from = null,
        [Description("Window end, ISO-8601 UTC. Omit for a week out.")] string? to = null)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(from)) q.Add($"from={Uri.EscapeDataString(from)}");
        if (!string.IsNullOrWhiteSpace(to)) q.Add($"to={Uri.EscapeDataString(to)}");
        var qs = q.Count > 0 ? "?" + string.Join("&", q) : "";
        return gw.GetAsync("san", $"/api/calendar/events{qs}");
    }

    // ── San: people ──

    [McpServerTool(Name = "person_create")]
    [Description("Add a person to the user's Maaya/San contacts.")]
    public Task<string> PersonCreate(
        [Description("Full name.")] string name,
        [Description("Phone number.")] string? phone = null,
        [Description("Email address.")] string? email = null,
        [Description("Birthday as yyyy-MM-dd (or MM-dd if the year is unknown).")] string? birthday = null,
        [Description("Relationship, e.g. family, friend, colleague (default other).")] string? relationship = null,
        [Description("Free-text notes.")] string? notes = null,
        [Description("Comma-separated tags.")] string? tags = null) =>
        gw.SendAsync("san", HttpMethod.Post, "/api/people",
            new { name, phone, email, birthday, relationship, notes, tags });

    [McpServerTool(Name = "person_update")]
    [Description("Update an existing contact in Maaya/San. Get the id from san_people. Send the full set of fields — omitted ones are overwritten.")]
    public Task<string> PersonUpdate(
        [Description("Person GUID from san_people.")] string personId,
        [Description("Full name.")] string name,
        [Description("Phone number.")] string? phone = null,
        [Description("Email address.")] string? email = null,
        [Description("Birthday as yyyy-MM-dd.")] string? birthday = null,
        [Description("Relationship.")] string? relationship = null,
        [Description("Free-text notes.")] string? notes = null,
        [Description("Comma-separated tags.")] string? tags = null) =>
        gw.SendAsync("san", HttpMethod.Put, $"/api/people/{personId}",
            new { name, phone, email, birthday, relationship, notes, tags });

    [McpServerTool(Name = "person_delete")]
    [Description("Delete a contact from Maaya/San. Get the id from san_people.")]
    public Task<string> PersonDelete(
        [Description("Person GUID.")] string personId) =>
        gw.SendAsync("san", HttpMethod.Delete, $"/api/people/{personId}", null);

    // ── Vitara: health logging ──

    [McpServerTool(Name = "food_log")]
    [Description("Log a food/meal to the user's Maaya/Vitara nutrition diary. USE THIS for 'I ate…' / 'log this food'. If you know the food's nutrition pass the per-100g macros so calories compute correctly; otherwise name + quantity is fine.")]
    public Task<string> FoodLog(
        [Description("Food name, e.g. 'Grilled chicken breast'.")] string foodName,
        [Description("breakfast | lunch | dinner | snack (default snack).")] string mealType = "snack",
        [Description("Day as yyyy-MM-dd local; omit for today.")] string? day = null,
        [Description("Quantity eaten (default 1).")] double qty = 1,
        [Description("Unit for qty, e.g. 'serving', 'g', 'cup'.")] string unit = "serving",
        [Description("Grams per serving, if known.")] double? servingSizeG = null,
        [Description("Calories per 100g, if known.")] double? calPer100 = null,
        [Description("Protein g per 100g, if known.")] double? protPer100 = null,
        [Description("Carbs g per 100g, if known.")] double? carbsPer100 = null,
        [Description("Fat g per 100g, if known.")] double? fatPer100 = null) =>
        gw.SendAsync("vitara", HttpMethod.Post, "/api/meals",
            new { day, mealType, foodName, qty, unit, servingSizeG, calPer100, protPer100, carbsPer100, fatPer100 });

    [McpServerTool(Name = "weight_log")]
    [Description("Record the user's body weight in Maaya/Vitara. The API stores KILOGRAMS — if the user says pounds, convert first (lb / 2.20462) and pass kg here.")]
    public Task<string> WeightLog(
        [Description("Weight in KILOGRAMS.")] double weightKg,
        [Description("Day as yyyy-MM-dd local; omit for today.")] string? day = null) =>
        gw.SendAsync("vitara", HttpMethod.Post, "/api/weighins", new { day, weightKg });

    [McpServerTool(Name = "workout_log")]
    [Description("Log a workout/exercise session to Maaya/Vitara. USE THIS for 'I ran…' / 'log my workout'.")]
    public Task<string> WorkoutLog(
        [Description("Activity, e.g. 'running', 'weights', 'cycling'.")] string activity,
        [Description("Day as yyyy-MM-dd local; omit for today.")] string? day = null,
        [Description("Calories burned, if known.")] int? calories = null,
        [Description("Distance in METRES, if applicable.")] int? distance = null,
        [Description("Intensity, e.g. 'easy', 'moderate', 'hard'.")] string? intensity = null,
        [Description("Optional label/notes.")] string? label = null) =>
        gw.SendAsync("vitara", HttpMethod.Post, "/api/workouts",
            new { day, activity, calories, distance, intensity, label });

    // ── Karma: habits & goals ──

    [McpServerTool(Name = "habit_checkin")]
    [Description("Mark one of the user's Maaya/Karma habits done (or not done) for a day. Call karma_habits FIRST to get the habit's id — this needs the GUID, not the name.")]
    public Task<string> HabitCheckin(
        [Description("Habit GUID from karma_habits.")] string habitId,
        [Description("True = completed, false = explicitly not done.")] bool completed = true,
        [Description("Day as yyyy-MM-dd local; omit for today.")] string? date = null,
        [Description("Optional note.")] string? note = null) =>
        gw.SendAsync("karma", HttpMethod.Post, $"/api/habits/{habitId}/log", new { date, completed, note });

    [McpServerTool(Name = "habit_create")]
    [Description("Create a new habit to track in Maaya/Karma.")]
    public Task<string> HabitCreate(
        [Description("Habit name, e.g. 'Morning walk'.")] string name,
        [Description("Optional description.")] string? description = null,
        [Description("A single emoji for the habit (default ✅).")] string emoji = "✅",
        [Description("Category, e.g. health, personal, work (default personal).")] string category = "personal",
        [Description("Daily nudge time as HH:mm (24h), e.g. '07:30'. Omit for no nudge.")] string? notifyTime = null,
        [Description("Message for the nudge.")] string? notifyMessage = null) =>
        gw.SendAsync("karma", HttpMethod.Post, "/api/habits",
            new { name, description, emoji, category, notifyTime, notifyMessage, notifyChannel = "telegram" });

    [McpServerTool(Name = "goals_list")]
    [Description("List the user's Maaya/Karma goals with their current progress — gives you the ids needed to update one.")]
    public Task<string> GoalsList() => gw.GetAsync("karma", "/api/goals");

    [McpServerTool(Name = "goal_create")]
    [Description("Create a new goal in Maaya/Karma.")]
    public Task<string> GoalCreate(
        [Description("Goal title, e.g. 'Reach 75kg'.")] string title,
        [Description("Optional description.")] string? description = null,
        [Description("Category, e.g. health, finance, career (default personal).")] string category = "personal",
        [Description("Target date as yyyy-MM-dd, if any.")] string? targetDate = null,
        [Description("Starting progress 0-100 (default 0).")] int progress = 0) =>
        gw.SendAsync("karma", HttpMethod.Post, "/api/goals",
            new { title, description, category, status = "active", progress, targetDate });

    [McpServerTool(Name = "goal_progress_set")]
    [Description("Set a Karma goal's progress percentage (0-100). Use this to keep goals in sync with real data — e.g. read the user's latest weight from Vitara, compute how far they are toward a weight goal, and update it here. Hitting 100 auto-completes the goal.")]
    public Task<string> GoalProgressSet(
        [Description("Goal GUID from goals_list.")] string goalId,
        [Description("Progress percent, 0-100.")] int progress) =>
        gw.SendAsync("karma", HttpMethod.Patch, $"/api/goals/{goalId}/progress", progress);

    // ── Aasthi: property financials ──

    [McpServerTool(Name = "property_financial_add")]
    [Description("Add an income or expense entry against one of the user's Maaya/Aasthi properties — e.g. after spotting a property-related transaction in Vault. Call aasthi_properties first to get the property id.")]
    public Task<string> PropertyFinancialAdd(
        [Description("Property GUID from aasthi_properties.")] string propertyId,
        [Description("'income' or 'expense'.")] string type,
        [Description("Category, e.g. rent, repairs, tax, insurance.")] string category,
        [Description("Amount, positive number.")] decimal amount,
        [Description("Date as yyyy-MM-dd.")] string date,
        [Description("Optional notes.")] string? notes = null) =>
        gw.SendAsync("aasthi", HttpMethod.Post, $"/api/properties/{propertyId}/financials",
            new { type, category, amount, date, notes });

    [McpServerTool(Name = "property_task_create")]
    [Description("Create a maintenance/to-do task against one of the user's Maaya/Aasthi properties — e.g. after spotting a property-related item in email or chat. Call aasthi_properties first to get the property id.")]
    public Task<string> PropertyTaskCreate(
        [Description("Property GUID from aasthi_properties.")] string propertyId,
        [Description("Task title, e.g. 'Fix leaking faucet'.")] string title,
        [Description("Due date as yyyy-MM-dd, if any.")] string? dueDate = null,
        [Description("low, medium, high, or urgent (default medium).")] string priority = "medium") =>
        gw.SendAsync("aasthi", HttpMethod.Post, "/api/tasks",
            new { propertyId, title, dueDate, priority });

    // ── NorthStar: keep the brain current ──

    [McpServerTool(Name = "northstar_sync")]
    [Description("Trigger a NorthStar sync — re-pulls every module's current state into the brain and distils it into knowledge entries. Run this before a review/briefing if the data looks stale (it also runs automatically every 15 minutes).")]
    public Task<string> NorthStarSync() =>
        gw.SendAsync("northstar", HttpMethod.Post, "/api/context/sync", new { });
}
