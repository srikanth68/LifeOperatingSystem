using System.Net.Http.Headers;
using System.Text.Json;
using Maaya.Auth;
using San.Application.Interfaces;

namespace San.Infrastructure.Agent;

// Every sibling module enforces Maaya's global JWT auth policy, so these calls MUST carry
// a Bearer token — same rule (and same minted service token) as ModuleContextService.
// Without it every tool call 401s, and since the raw body was previously returned to the
// model regardless of status, San would present an auth error as "no data available"
// (e.g. reporting no sleep data while Vitara held it all along).
public class AgentToolExecutor(IHttpClientFactory httpFactory, ITelegramNotifier telegram, IChatActionService actions, TokenService tokens)
{
    private string ServiceToken() => tokens.GenerateAccessToken("san-service", "san");

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
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ServiceToken());
        using var resp = await http.SendAsync(req, ct);
        return await ReadOrFailAsync(resp, client, path, ct);
    }

    private async Task<string> PostJson(string client, string path, object body, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(client);
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ServiceToken());
        using var resp = await http.SendAsync(req, ct);
        return await ReadOrFailAsync(resp, client, path, ct);
    }

    // A failed call must announce itself. Returning the raw body regardless of status
    // let error pages reach the model as if they were data, which it then reported as
    // "nothing found" — a silent wrong answer is worse than a visible failure.
    private static async Task<string> ReadOrFailAsync(HttpResponseMessage resp, string client, string path, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (resp.IsSuccessStatusCode) return body;
        var detail = body.Length > 200 ? body[..200] : body;
        return $"Tool error: {client} returned HTTP {(int)resp.StatusCode} for {path}. " +
               $"This is a system/connection failure, NOT an absence of data — do not tell the user the data doesn't exist. Detail: {detail}";
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
