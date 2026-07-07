using San.Application.Interfaces;

namespace San.Infrastructure.Agent;

public class AgentToolRegistry
{
    public static List<ToolDefinition> GetTools() =>
    [
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
