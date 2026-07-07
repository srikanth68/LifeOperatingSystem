using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using San.Application.Interfaces;

namespace San.Infrastructure.Llm;

public class GeminiChatProvider(HttpClient http, IConfiguration config) : IChatProvider
{
    public string ProviderName => "gemini";
    public string ModelName => config["Llm:Model"] ?? "gemini-2.0-flash";

    private string ApiKey => Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";

    public async Task<string> CompleteAsync(string systemPrompt, List<ChatTurn> history, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return "(San can't reach Gemini — GEMINI_API_KEY isn't set in san/.env. Get one at aistudio.google.com)";

        var contents = new List<object>();

        foreach (var turn in history)
        {
            contents.Add(new
            {
                role = turn.Role == "assistant" ? "model" : "user",
                parts = new[] { new { text = turn.Content } }
            });
        }

        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents,
            generationConfig = new { maxOutputTokens = 2048 }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{ModelName}:generateContent?key={ApiKey}";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            return $"(San hit an error calling Gemini: {resp.StatusCode} — {body})";

        using var doc = JsonDocument.Parse(body);
        var candidates = doc.RootElement.GetProperty("candidates");
        if (candidates.GetArrayLength() == 0) return "(Gemini returned no response)";

        var parts = candidates[0].GetProperty("content").GetProperty("parts");
        if (parts.GetArrayLength() == 0) return "(empty response)";

        return parts[0].GetProperty("text").GetString() ?? "(empty response)";
    }
}
