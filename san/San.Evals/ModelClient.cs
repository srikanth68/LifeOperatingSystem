using System.Text;
using System.Text.Json;

namespace San.Evals;

// Talks to the same llama.cpp server San talks to.
//
// Deliberately NOT routed through San's API. Going through the module would measure
// San plus its prompt assembly plus its tool loop plus whatever NorthStar happened to
// recall this morning -- and when the number moved, nothing would say which of those
// moved it. This asks the model a question and reads the answer.
public sealed class ModelClient(string baseUrl, double temperature, bool enableThinking)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public async Task<string> AskAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage },
            },
            ["temperature"] = temperature,
            ["max_tokens"] = 400,
            // Matches interactive chat, which runs with deliberation off. Measuring with
            // thinking on would score a configuration the user never actually uses.
            ["chat_template_kwargs"] = new Dictionary<string, object> { ["enable_thinking"] = enableThinking },
            // Every case is pinned to one slot so runs share a prompt cache rather than
            // evicting each other -- the suite is a few hundred calls against the same
            // handful of system prompts.
            ["id_slot"] = 0,
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync($"{baseUrl.TrimEnd('/')}/v1/chat/completions", content, ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"llama.cpp returned HTTP {(int)resp.StatusCode}.");

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
    }

    // Confirms the endpoint is up and reports which model answered, so a scoreboard is
    // never silently compared against a different set of weights than it was recorded
    // with. Comparing a fine-tune to a baseline taken on the base model is the whole
    // point; comparing it to a baseline taken on an unknown model is worthless.
    public async Task<string> ModelNameAsync(CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync($"{baseUrl.TrimEnd('/')}/props", ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var path = doc.RootElement.TryGetProperty("model_path", out var m) ? m.GetString() ?? "" : "";
        return string.IsNullOrEmpty(path) ? "unknown" : Path.GetFileName(path);
    }
}
