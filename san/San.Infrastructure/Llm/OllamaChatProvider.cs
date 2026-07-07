using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using San.Application.Interfaces;

namespace San.Infrastructure.Llm;

public class OllamaChatProvider(HttpClient http, IConfiguration config) : IChatProvider
{
    public string ProviderName => "ollama";
    public string ModelName => config["Llm:Model"] ?? "gemma3:4b";

    private string BaseUrl => Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434";

    public async Task<string> CompleteAsync(string systemPrompt, List<ChatTurn> history, CancellationToken ct = default)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };
        messages.AddRange(history.Select(h => (object)new { role = h.Role, content = h.Content }));

        var payload = new
        {
            model = ModelName,
            messages,
            stream = false,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/chat");
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            return $"(San can't reach Ollama at {BaseUrl} — is it running? Error: {resp.StatusCode})";

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "(empty response)";
    }
}
