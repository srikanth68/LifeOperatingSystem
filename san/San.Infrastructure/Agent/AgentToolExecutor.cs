using System.Text.Json;
using San.Application.Interfaces;

namespace San.Infrastructure.Agent;

public class AgentToolExecutor(IHttpClientFactory httpFactory, ITelegramNotifier telegram, IChatActionService actions)
{
    public async Task<string> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        try
        {
            return call.Name switch
            {
                // San's own actions share the prose-path implementation (validation,
                // timezone conversion) via the action service.
                "create_reminder" or "create_alert" or "create_calendar_event"
                    => await actions.ExecuteToolCallAsync(call, ct),
                "get_health_summary" => await GetJson("vitara", "/api/dashboard", ct),
                "get_budget_summary" => await GetJson("vault", "/api/summary", ct),
                "get_property_tasks" => await GetJson("aasthi", "/api/tasks" + QueryString(call, "status"), ct),
                "create_task" => await PostJson("aasthi", "/api/tasks", call.Arguments, ct),
                "search_knowledge" => await GetJson("northstar", $"/api/knowledge/search?q={Uri.EscapeDataString(call.Arguments.GetValueOrDefault("query", ""))}", ct),
                "save_knowledge" => await PostJson("northstar", "/api/ingest", new
                {
                    source = "san",
                    topic = call.Arguments.GetValueOrDefault("topic", "general"),
                    summary = call.Arguments.GetValueOrDefault("summary", "")
                }, ct),
                "send_notification" => await SendNotification(call.Arguments.GetValueOrDefault("message", ""), ct),
                _ => $"Unknown tool: {call.Name}"
            };
        }
        catch (Exception ex)
        {
            return $"Tool error ({call.Name}): {ex.Message}";
        }
    }

    private async Task<string> GetJson(string client, string path, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(client);
        using var resp = await http.GetAsync(path, ct);
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> PostJson(string client, string path, object body, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(client);
        var content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
        using var resp = await http.PostAsync(path, content, ct);
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private static string QueryString(ToolCall call, string param)
    {
        var val = call.Arguments.GetValueOrDefault(param, "");
        return string.IsNullOrWhiteSpace(val) ? "" : $"?{param}={Uri.EscapeDataString(val)}";
    }

    private async Task<string> SendNotification(string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message)) return "No message provided.";
        await telegram.SendAsync(message, ct);
        return "Notification sent.";
    }
}
