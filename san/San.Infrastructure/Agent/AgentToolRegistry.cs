using San.Application.Interfaces;

namespace San.Infrastructure.Agent;

public class AgentToolRegistry
{
    // Shared date guidance for every wall-clock parameter — the native-tools path
    // doesn't get the prose ToolInstructions, so the format rule must live on the
    // parameters themselves. The current local date/time to anchor "today" and
    // "tomorrow" against is in the system prompt's time context.
    private const string LocalTime =
        "LOCAL wall-clock time formatted exactly yyyy-MM-ddTHH:mm:ss — no 'Z', no UTC offset. " +
        "Anchor relative dates (today, tomorrow) on the current date given in your time context.";

    public static List<ToolDefinition> GetTools() =>
    [
        // ── San's own actions (same funnel as the prose ```action path) ──
        new("create_reminder", "Create a reminder for the user", new Dictionary<string, ToolParameter>
        {
            ["text"] = new("string", "What to remind the user about", true),
            ["dueAt"] = new("string", $"When the reminder is due. {LocalTime}", true)
        }),
        new("create_alert", "Create a custom alert for the user", new Dictionary<string, ToolParameter>
        {
            ["type"] = new("string", "One of: spending_threshold, goal_deadline, document_expiry, custom", true),
            ["title"] = new("string", "Short alert title", true),
            ["description"] = new("string", "Optional longer description"),
            ["thresholdValue"] = new("number", "Dollar amount — required only for spending_threshold"),
            ["triggerAt"] = new("string", $"When the alert fires — required for every type except spending_threshold. {LocalTime}")
        }),
        new("create_calendar_event", "Create a calendar event for the user", new Dictionary<string, ToolParameter>
        {
            ["title"] = new("string", "Event title", true),
            ["startTime"] = new("string", $"Event start. {LocalTime}", true),
            ["endTime"] = new("string", $"Event end. {LocalTime}", true),
            ["description"] = new("string", "Optional details"),
            ["location"] = new("string", "Optional location")
        }),

        // ── Cross-module reads/writes ──
        new("get_health_summary", "Get user's recent health metrics from Vitara (sleep, readiness, activity scores)", new()),
        new("get_budget_summary", "Get user's financial summary from Vault (net worth, cash, spending)", new()),
        new("get_property_tasks", "Get property tasks from Aasthi, optionally filtered by status", new Dictionary<string, ToolParameter>
        {
            ["status"] = new("string", "Filter by status: pending, in_progress, completed, cancelled")
        }),
        new("create_task", "Create a new property task in Aasthi", new Dictionary<string, ToolParameter>
        {
            ["propertyId"] = new("string", "GUID of the property", true),
            ["title"] = new("string", "Task title", true),
            ["dueDate"] = new("string", "Due date in yyyy-MM-dd format"),
            ["priority"] = new("string", "low, medium, high, or urgent")
        }),
        new("search_knowledge", "Search NorthStar knowledge base for past insights and data", new Dictionary<string, ToolParameter>
        {
            ["query"] = new("string", "Search query", true)
        }),
        new("save_knowledge", "Save a new knowledge entry to NorthStar", new Dictionary<string, ToolParameter>
        {
            ["topic"] = new("string", "Topic category: health, spending, property, task, general", true),
            ["summary"] = new("string", "Summary text to store", true)
        }),
        new("send_notification", "Send a Telegram notification to the user", new Dictionary<string, ToolParameter>
        {
            ["message"] = new("string", "Message text to send", true)
        })
    ];
}
