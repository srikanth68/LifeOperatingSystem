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
    [Description("Create a reminder. Use for ANY \"remind me...\" - never Apple Reminders, never action_add (that's silent backlog). text* · dueAt* · notifyTelegram (true)")]
    public async Task<string> ReminderCreate(
        string text,
        string dueAt,
        bool notifyTelegram = true)
    {
        var (utc, error) = await ToUtcAsync(dueAt, "dueAt", rejectPast: true);
        if (error is not null) return Fail(error);
        return await gw.SendAsync("san", HttpMethod.Post, "/api/reminders",
            new { text, dueAt = utc, notifyTelegram });
    }

    [McpServerTool(Name = "reminders_list")]
    [Description("Reminders with ids for update/complete/delete.")]
    public Task<string> RemindersList() => gw.GetAsync("san", "/api/reminders");

    [McpServerTool(Name = "reminder_update")]
    [Description("Edit text or due time; re-arms the notification. reminderId* · text* · dueAt* · notifyTelegram")]
    public async Task<string> ReminderUpdate(
        string reminderId,
        string text,
        string dueAt,
        bool notifyTelegram = true)
    {
        var (utc, error) = await ToUtcAsync(dueAt, "dueAt", rejectPast: true);
        if (error is not null) return Fail(error);
        return await gw.SendAsync("san", HttpMethod.Put, $"/api/reminders/{reminderId}",
            new { text, dueAt = utc, notifyTelegram });
    }

    [McpServerTool(Name = "reminder_complete")]
    [Description("Tick off / reopen a reminder. reminderId* · done")]
    public Task<string> ReminderComplete(
        string reminderId,
        bool done = true) =>
        gw.SendAsync("san", HttpMethod.Patch, $"/api/reminders/{reminderId}/done", done);

    [McpServerTool(Name = "reminder_delete")]
    [Description("Permanently delete. Prefer reminder_complete unless removal is explicit. reminderId*")]
    public Task<string> ReminderDelete(
        string reminderId) =>
        gw.SendAsync("san", HttpMethod.Delete, $"/api/reminders/{reminderId}", null);

    // ── San: alerts ──

    [McpServerTool(Name = "alert_create")]
    [Description("Create an alert. type* spending_threshold | goal_deadline | document_expiry | custom · title* · description thresholdValue dollars - required for spending_threshold only triggerAt - required for all other types notifyTelegram (true)")]
    public async Task<string> AlertCreate(
        string type,
        string title,
        string? description = null,
        decimal? thresholdValue = null,
        string? triggerAt = null,
        bool notifyTelegram = true)
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
    [Description("Alerts with ids. For \"what alerts do I have\". Active ones already appear in agenda_now.")]
    public Task<string> AlertsList() => gw.GetAsync("san", "/api/alerts");

    [McpServerTool(Name = "alert_delete")]
    [Description("- alertId* from alerts_list.")]
    public Task<string> AlertDelete(
        string alertId) =>
        gw.SendAsync("san", HttpMethod.Delete, $"/api/alerts/{alertId}", null);

    // ── San: calendar ──

    [McpServerTool(Name = "calendar_event_create")]
    [Description("Create a calendar event. Use for \"schedule...\", \"put it on my calendar\" - never an external calendar app. title* · startTime* · endTime* · description · location")]
    public async Task<string> CalendarEventCreate(
        string title,
        string startTime,
        string endTime,
        string? description = null,
        string? location = null)
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
    [Description("Upcoming events in a window. For \"what's on my calendar\", \"am I free on X\", \"when is Y\". General \"what should I be doing\" -> agenda_now. from (today) · to (+1 week)")]
    public Task<string> CalendarEventsList(
        string? from = null,
        string? to = null)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(from)) q.Add($"from={Uri.EscapeDataString(from)}");
        if (!string.IsNullOrWhiteSpace(to)) q.Add($"to={Uri.EscapeDataString(to)}");
        var qs = q.Count > 0 ? "?" + string.Join("&", q) : "";
        return gw.GetAsync("san", $"/api/calendar/events{qs}");
    }

    // ── San: people ──

    [McpServerTool(Name = "person_create")]
    [Description("Add a contact. name* · phone · email · birthday yyyy-MM-dd or MM-dd · relationship (other) · notes · tags csv")]
    public Task<string> PersonCreate(
        string name,
        string? phone = null,
        string? email = null,
        string? birthday = null,
        string? relationship = null,
        string? notes = null,
        string? tags = null) =>
        gw.SendAsync("san", HttpMethod.Post, "/api/people",
            new { name, phone, email, birthday, relationship, notes, tags });

    [McpServerTool(Name = "person_update")]
    [Description("Update a contact. Send ALL fields - omitted ones are overwritten. personId* · name* · phone · email · birthday · relationship · notes · tags")]
    public Task<string> PersonUpdate(
        string personId,
        string name,
        string? phone = null,
        string? email = null,
        string? birthday = null,
        string? relationship = null,
        string? notes = null,
        string? tags = null) =>
        gw.SendAsync("san", HttpMethod.Put, $"/api/people/{personId}",
            new { name, phone, email, birthday, relationship, notes, tags });

    [McpServerTool(Name = "person_delete")]
    [Description("- personId* from san_people.")]
    public Task<string> PersonDelete(
        string personId) =>
        gw.SendAsync("san", HttpMethod.Delete, $"/api/people/{personId}", null);

    // ── Vitara: health logging ──

    [McpServerTool(Name = "food_log")]
    [Description("Log a meal to Vitara nutrition. For \"I ate...\". Pass per-100g macros if known so calories compute; otherwise name + qty is fine. foodName* · mealType breakfast|lunch|dinner|snack (snack) · day · qty (1) · unit · servingSizeG · calPer100 · protPer100 · carbsPer100 · fatPer100")]
    public Task<string> FoodLog(
        string foodName,
        string mealType = "snack",
        string? day = null,
        double qty = 1,
        string unit = "serving",
        double? servingSizeG = null,
        double? calPer100 = null,
        double? protPer100 = null,
        double? carbsPer100 = null,
        double? fatPer100 = null) =>
        gw.SendAsync("vitara", HttpMethod.Post, "/api/meals",
            new { day, mealType, foodName, qty, unit, servingSizeG, calPer100, protPer100, carbsPer100, fatPer100 });

    [McpServerTool(Name = "weight_log")]
    [Description("Record body weight. API stores KILOGRAMS - convert pounds (lb / 2.20462) before sending. weightKg* · day")]
    public Task<string> WeightLog(
        double weightKg,
        string? day = null) =>
        gw.SendAsync("vitara", HttpMethod.Post, "/api/weighins", new { day, weightKg });

    [McpServerTool(Name = "workout_log")]
    [Description("Log a workout. For \"I ran...\", \"log my workout\". activity* · day · calories · distance METRES · intensity · label")]
    public Task<string> WorkoutLog(
        string activity,
        string? day = null,
        int? calories = null,
        int? distance = null,
        string? intensity = null,
        string? label = null) =>
        gw.SendAsync("vitara", HttpMethod.Post, "/api/workouts",
            new { day, activity, calories, distance, intensity, label });

    // ── Karma: habits & goals ──

    [McpServerTool(Name = "habit_checkin")]
    [Description("Mark a habit done/not-done. Needs the GUID - call karma_habits first. habitId* · completed · date · note")]
    public Task<string> HabitCheckin(
        string habitId,
        bool completed = true,
        string? date = null,
        string? note = null) =>
        gw.SendAsync("karma", HttpMethod.Post, $"/api/habits/{habitId}/log", new { date, completed, note });

    [McpServerTool(Name = "habit_create")]
    [Description("Karma habit - a repeating action (\"meditate every morning\"). One-off outcome -> goal_create. name* · description · emoji (✅) · category (personal) · notifyTime HH:mm · notifyMessage")]
    public Task<string> HabitCreate(
        string name,
        string? description = null,
        string emoji = "✅",
        string category = "personal",
        string? notifyTime = null,
        string? notifyMessage = null) =>
        gw.SendAsync("karma", HttpMethod.Post, "/api/habits",
            new { name, description, emoji, category, notifyTime, notifyMessage, notifyChannel = "telegram" });

    [McpServerTool(Name = "goals_list")]
    [Description("Karma goals with progress and ids. For \"what are my goals\", \"how far along am I\".")]
    public Task<string> GoalsList() => gw.GetAsync("karma", "/api/goals");

    [McpServerTool(Name = "goal_create")]
    [Description("Karma goal - an outcome over time (\"run a half marathon\"). Repeating daily action -> habit_create. title* · description · category (personal) · targetDate · progress (0)")]
    public Task<string> GoalCreate(
        string title,
        string? description = null,
        string category = "personal",
        string? targetDate = null,
        int progress = 0) =>
        gw.SendAsync("karma", HttpMethod.Post, "/api/goals",
            new { title, description, category, status = "active", progress, targetDate });

    [McpServerTool(Name = "goal_progress_set")]
    [Description("Set goal progress 0-100; 100 auto-completes. Use to sync goals with real data (e.g. Vitara weight -> weight goal). goalId* · progress*")]
    public Task<string> GoalProgressSet(
        string goalId,
        int progress) =>
        gw.SendAsync("karma", HttpMethod.Patch, $"/api/goals/{goalId}/progress", progress);

    // ── Aasthi: property financials ──

    [McpServerTool(Name = "property_financial_add")]
    [Description("Add income/expense against a property. Call aasthi_properties first for the id. propertyId* · type* income|expense · category* rent|repairs|tax|insurance|... · amount* positive · date* · notes")]
    public Task<string> PropertyFinancialAdd(
        string propertyId,
        string type,
        string category,
        decimal amount,
        string date,
        string? notes = null) =>
        gw.SendAsync("aasthi", HttpMethod.Post, $"/api/properties/{propertyId}/financials",
            new { type, category, amount, date, notes });

    [McpServerTool(Name = "property_task_create")]
    [Description("Create a maintenance task against a property. Call aasthi_properties first for the id. propertyId* · title* · dueDate · priority low|medium|high|urgent (medium)")]
    public Task<string> PropertyTaskCreate(
        string propertyId,
        string title,
        string? dueDate = null,
        string priority = "medium") =>
        gw.SendAsync("aasthi", HttpMethod.Post, "/api/tasks",
            new { propertyId, title, dueDate, priority });

    // ── NorthStar: keep the brain current ──

    [McpServerTool(Name = "northstar_sync")]
    [Description("Re-pull every module's state into the brain and distil to knowledge entries. Run before a review/briefing if data looks stale (also auto-runs every 15 min).")]
    public Task<string> NorthStarSync() =>
        gw.SendAsync("northstar", HttpMethod.Post, "/api/context/sync", new { });
}
