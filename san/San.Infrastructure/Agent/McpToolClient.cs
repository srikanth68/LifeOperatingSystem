using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using San.Application.Interfaces;

namespace San.Infrastructure.Agent;

// Minimal MCP (Model Context Protocol) client over streamable HTTP, giving San's
// agent loop the ENTIRE Maaya.Mcp tool catalog (~40 tools: reminders, alerts,
// calendar, people, food/weight/workout logging, habits, goals, module reads,
// NorthStar memory…) instead of the 10 hand-mirrored in AgentToolRegistry.
// New tools added to Maaya.Mcp appear in San's chat automatically.
//
// Speaks just the three JSON-RPC methods the loop needs — initialize,
// tools/list, tools/call — over POST {MCP_BASE_URL}/, handling both plain-JSON
// and SSE-wrapped responses (the C# MCP SDK answers POSTs with text/event-stream).
//
// Config (env):
//   MCP_BASE_URL=http://mcp:5900        (compose service; gateway on the same network)
//   MCP_API_KEY=<MCP_API_KEY from deploy/env/mcp.env — required if the gateway has one>
public class McpToolClient(HttpClient http, ILogger<McpToolClient> logger)
{
    private const string ProtocolVersion = "2025-03-26";
    private static readonly TimeSpan ToolsCacheTtl = TimeSpan.FromMinutes(5);

    // Session + tool-list cache shared across requests (the client is re-created
    // per request by the typed-HttpClient factory, so state must live statically).
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _sessionId;
    private static List<ToolDefinition>? _tools;
    private static Dictionary<string, Dictionary<string, string>>? _paramTypes; // tool -> param -> json type
    private static DateTime _toolsFetchedAt = DateTime.MinValue;
    private static int _nextId = 1;

    private static string BaseUrl =>
        (Environment.GetEnvironmentVariable("MCP_BASE_URL") ?? "http://mcp:5900").TrimEnd('/');

    private static string? ApiKey => Environment.GetEnvironmentVariable("MCP_API_KEY");

    // Null when the gateway is unreachable/misconfigured — caller falls back to
    // the local registry so chat keeps working without the MCP container.
    public async Task<List<ToolDefinition>?> TryListToolsAsync(CancellationToken ct = default)
    {
        if (_tools is not null && DateTime.UtcNow - _toolsFetchedAt < ToolsCacheTtl)
            return _tools;

        await Gate.WaitAsync(ct);
        try
        {
            if (_tools is not null && DateTime.UtcNow - _toolsFetchedAt < ToolsCacheTtl)
                return _tools;

            var result = await RpcAsync("tools/list", new { }, ct);
            if (result is null) return _tools; // stale list beats none

            var tools = new List<ToolDefinition>();
            var types = new Dictionary<string, Dictionary<string, string>>();
            foreach (var t in result.Value.GetProperty("tools").EnumerateArray())
            {
                var name = t.GetProperty("name").GetString() ?? "";
                var desc = t.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                var parameters = new Dictionary<string, ToolParameter>();
                var paramTypes = new Dictionary<string, string>();

                if (t.TryGetProperty("inputSchema", out var schema))
                {
                    var required = new HashSet<string>();
                    if (schema.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.Array)
                        foreach (var r in reqEl.EnumerateArray())
                            if (r.GetString() is { } s) required.Add(s);

                    if (schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
                        foreach (var p in props.EnumerateObject())
                        {
                            var pType = p.Value.TryGetProperty("type", out var pt) ? pt.GetString() ?? "string" : "string";
                            var pDesc = p.Value.TryGetProperty("description", out var pd) ? pd.GetString() ?? "" : "";
                            parameters[p.Name] = new ToolParameter(pType, pDesc, required.Contains(p.Name));
                            paramTypes[p.Name] = pType;
                        }
                }

                tools.Add(new ToolDefinition(name, desc, parameters));
                types[name] = paramTypes;
            }

            _tools = tools;
            _paramTypes = types;
            _toolsFetchedAt = DateTime.UtcNow;
            return tools;
        }
        catch (Exception ex)
        {
            // Falling back to the built-in registry is a real capability drop (~40 tools
            // down to 10), so the reason has to be visible — this was silent before.
            logger.LogWarning(ex, "MCP tools/list failed against {BaseUrl} (api key {KeyState})",
                BaseUrl, string.IsNullOrWhiteSpace(ApiKey) ? "NOT set" : "set");
            return _tools; // unreachable — serve stale if we have it, else null
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<string> CallToolAsync(ToolCall call, CancellationToken ct = default)
    {
        // Coerce string args back to the JSON types the tool's schema declares —
        // sending "5" where a number is expected fails SDK model binding.
        var typed = new Dictionary<string, object?>();
        var schema = _paramTypes?.GetValueOrDefault(call.Name);
        foreach (var (k, v) in call.Arguments)
            typed[k] = (schema?.GetValueOrDefault(k)) switch
            {
                "number" when decimal.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var dec) => dec,
                "integer" when long.TryParse(v, out var l) => l,
                "boolean" when bool.TryParse(v, out var b) => b,
                _ => v,
            };

        JsonElement? result;
        try
        {
            result = await RpcAsync("tools/call", new { name = call.Name, arguments = typed }, ct);
        }
        catch (Exception ex)
        {
            return $"Tool error ({call.Name}): MCP gateway call failed — {ex.Message}";
        }
        if (result is null) return $"Tool error ({call.Name}): MCP gateway didn't answer.";

        var isError = result.Value.TryGetProperty("isError", out var ie) && ie.ValueKind == JsonValueKind.True;
        var text = new StringBuilder();
        if (result.Value.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            foreach (var item in content.EnumerateArray())
                if (item.TryGetProperty("text", out var txt))
                    text.AppendLine(txt.GetString());

        var body = text.ToString().Trim();
        if (body.Length == 0) body = isError ? "unknown error" : $"{call.Name}: done.";
        return isError ? $"Tool error ({call.Name}): {body}" : body;
    }

    // ── JSON-RPC plumbing ─────────────────────────────────────────────────────

    private async Task<JsonElement?> RpcAsync(string method, object @params, CancellationToken ct)
    {
        var attempt = await RpcOnceAsync(method, @params, ct);
        if (attempt is not null) return attempt;

        // One retry with a fresh session — covers the gateway having restarted
        // (its session ids don't survive) without failing the user's turn.
        _sessionId = null;
        return await RpcOnceAsync(method, @params, ct);
    }

    private async Task<JsonElement?> RpcOnceAsync(string method, object @params, CancellationToken ct)
    {
        if (_sessionId is null && method != "initialize" && !await InitializeAsync(ct))
            return null;

        var id = Interlocked.Increment(ref _nextId);
        var resp = await PostAsync(new { jsonrpc = "2.0", id, method, @params }, ct);
        if (resp is null) return null;

        if (resp.Value.TryGetProperty("error", out var err))
            throw new InvalidOperationException(err.TryGetProperty("message", out var m) ? m.GetString() : "MCP error");
        return resp.Value.TryGetProperty("result", out var result) ? result.Clone() : null;
    }

    private async Task<bool> InitializeAsync(CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        var resp = await PostAsync(new
        {
            jsonrpc = "2.0",
            id,
            method = "initialize",
            @params = new
            {
                protocolVersion = ProtocolVersion,
                capabilities = new { },
                clientInfo = new { name = "san-agent", version = "1.0" },
            },
        }, ct, captureSession: true);
        if (resp is null || resp.Value.TryGetProperty("error", out _)) return false;

        // Spec: client must acknowledge before issuing further requests.
        await PostAsync(new { jsonrpc = "2.0", method = "notifications/initialized" }, ct);
        return true;
    }

    private async Task<JsonElement?> PostAsync(object payload, CancellationToken ct, bool captureSession = false)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/");
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        // Streamable HTTP requires advertising both response modes.
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.Accept.ParseAdd("text/event-stream");
        if (_sessionId is { } sid) req.Headers.Add("Mcp-Session-Id", sid);
        if (!string.IsNullOrWhiteSpace(ApiKey))
            req.Headers.Add("X-API-Key", ApiKey);

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            // 401 here almost always means MCP_API_KEY differs between deploy/env/san.env
            // and deploy/env/mcp.env; 404 means MCP_BASE_URL points somewhere wrong.
            var detail = await resp.Content.ReadAsStringAsync(ct);
            logger.LogWarning("MCP POST {BaseUrl}/ returned HTTP {Status}. {Detail}",
                BaseUrl, (int)resp.StatusCode, detail.Length > 200 ? detail[..200] : detail);
            return null;
        }

        if (captureSession && resp.Headers.TryGetValues("Mcp-Session-Id", out var vals))
            _sessionId = vals.FirstOrDefault();

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body)) return null; // notifications get 202/empty

        // SSE framing: take the last `data:` line (the response message).
        if ((resp.Content.Headers.ContentType?.MediaType ?? "").Contains("event-stream"))
        {
            string? data = null;
            foreach (var line in body.Split('\n'))
                if (line.StartsWith("data:", StringComparison.Ordinal))
                    data = line[5..].Trim();
            if (data is null) return null;
            body = data;
        }

        return JsonDocument.Parse(body).RootElement.Clone();
    }
}
