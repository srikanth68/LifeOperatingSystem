using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using San.Application.Interfaces;

namespace San.Infrastructure.Llm;

// One IChatProvider implementation. The active provider + model are selected purely by
// config (Llm:Provider / Llm:Model, see Program.cs) — adding another provider later means
// implementing IChatProvider and adding one switch case in Program.cs, no changes here.
public class AnthropicChatProvider(HttpClient http, IConfiguration config) : IChatProvider
{
    public string ProviderName => "anthropic";
    public string ModelName => config["Llm:Model"] ?? "claude-sonnet-4-6";

    private string ApiKey => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";

    public async Task<string> CompleteAsync(string systemPrompt, List<ChatTurn> history, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return "(San can't reach Claude yet — ANTHROPIC_API_KEY isn't set in san/.env. Add it and restart San.API.)";

        var payload = new
        {
            model = ModelName,
            max_tokens = 1024,
            system = systemPrompt,
            messages = history.Select(h => new { role = h.Role, content = h.Content }).ToList(),
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.Add("x-api-key", ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            return $"(San hit an error calling Claude: {resp.StatusCode} — {body})";

        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
        return text ?? "(empty response)";
    }
}
