using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Maaya.Auth;

namespace Maaya.Mcp;

// Thin authenticated HTTP bridge to every Maaya module. Each call mints a service
// JWT (shared secret — validates on all modules), enforces a short timeout, and
// caps response size so tool output never floods an agent's context window.
public sealed class ModuleGateway(IHttpClientFactory http, TokenService tokens)
{
    private const int MaxChars = 6000;

    public Task<string> GetAsync(string module, string path) =>
        SendAsync(module, HttpMethod.Get, path, null);

    public async Task<string> SendAsync(string module, HttpMethod method, string path, object? body)
    {
        try
        {
            var client = http.CreateClient(module);
            using var req = new HttpRequestMessage(method, path);
            req.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", tokens.GenerateAccessToken("mcp-gateway", "mcp"));
            if (body is not null)
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var resp = await client.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return Error($"{module} returned HTTP {(int)resp.StatusCode}", Trim(text, 300));

            return Trim(text, MaxChars);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Error($"{module} is unreachable (offline or not started)", ex.Message);
        }
    }

    // Parallel health probe across all modules — cheap aliveness + latency.
    public async Task<string> ProbeAllAsync(IReadOnlyDictionary<string, string> probes)
    {
        var results = await Task.WhenAll(probes.Select(async p =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var client = http.CreateClient(p.Key);
                using var req = new HttpRequestMessage(HttpMethod.Get, p.Value);
                req.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer", tokens.GenerateAccessToken("mcp-gateway", "mcp"));
                using var resp = await client.SendAsync(req);
                return new { module = p.Key, online = resp.IsSuccessStatusCode, ms = sw.ElapsedMilliseconds };
            }
            catch
            {
                return new { module = p.Key, online = false, ms = sw.ElapsedMilliseconds };
            }
        }));
        return JsonSerializer.Serialize(new
        {
            online = results.Count(r => r.online),
            total = results.Length,
            modules = results,
        });
    }

    private static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..max] + $"\n…[truncated, {s.Length} chars total]";

    private static string Error(string message, string? detail = null) =>
        JsonSerializer.Serialize(new { error = message, detail });
}
