using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using San.Application.Interfaces;

namespace San.Infrastructure.Llm;

// Routes San's chat to Hermes Agent's OpenAI-compatible gateway (`hermes gateway`,
// default http://127.0.0.1:8642). Hermes runs the SAME local Gemma model San would,
// but inside a real agent harness with genuine tool-calling — so it can reliably DO
// things (create reminders, etc.) via the Maaya MCP tools, which San's own
// prose-JSON approach can't. This keeps everything local: San → Hermes → llama.cpp
// + Maaya.Mcp, no cloud.
//
// Config (env):
//   LLM_PROVIDER=hermes
//   HERMES_BASE_URL=http://host.docker.internal:8642   (San is in Docker; Hermes on the host)
//   HERMES_API_KEY=<the API_SERVER_KEY you set in ~/.hermes/.env>
//   HERMES_MODEL=hermes-agent
//
// Requires, on the Everest host: `API_SERVER_ENABLED=true` in ~/.hermes/.env, the
// gateway running (`hermes gateway`), and Hermes configured with Maaya.Mcp (port
// 5900) as an MCP server so its toolset includes reminder_create/alert_create/etc.
public class HermesChatProvider(HttpClient http, IConfiguration config) : IChatProvider
{
    public string ProviderName => "hermes";
    public string ModelName => Environment.GetEnvironmentVariable("HERMES_MODEL")
                               ?? config["Llm:Model"] ?? "hermes-agent";

    // Hermes executes tools itself — San must not add its own action-block scaffolding.
    public bool HandlesToolsNatively => true;

    private static string BaseUrl =>
        (Environment.GetEnvironmentVariable("HERMES_BASE_URL")
         ?? Environment.GetEnvironmentVariable("LLM_BASE_URL")
         ?? "http://host.docker.internal:8642").TrimEnd('/');

    public async Task<string> CompleteAsync(string systemPrompt, List<ChatTurn> history, CancellationToken ct = default)
    {
        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        messages.AddRange(history.Select(h => (object)new { role = h.Role, content = h.Content }));

        var payload = new
        {
            model = ModelName,
            messages,
            stream = false,   // Hermes runs its full agent loop, then returns the final answer
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/chat/completions");
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var apiKey = Environment.GetEnvironmentVariable("HERMES_API_KEY")
                     ?? Environment.GetEnvironmentVariable("LLM_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct);
        }
        catch
        {
            // Hermes gateway unreachable (not started, or ~/.hermes/.env not enabled).
            // Stay local — never fall back to a cloud LLM with private data in the prompt.
            return Offline;
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            return $"⚠️ Hermes returned an error ({(int)resp.StatusCode}). Nothing left your machine. Detail: {Trim(body)}";

        try
        {
            var reply = JsonDocument.Parse(body).RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return string.IsNullOrWhiteSpace(reply)
                ? "🤔 Hermes returned an empty answer. Check the gateway logs on Everest."
                : reply;
        }
        catch
        {
            return "⚠️ San got an unexpected response shape from Hermes. Nothing left your machine.";
        }
    }

    private static string Trim(string s) => s.Length <= 300 ? s : s[..300];

    private const string Offline =
        "🔌 San can't reach Hermes right now — the gateway on Everest looks off. " +
        "Enable it (API_SERVER_ENABLED=true in ~/.hermes/.env) and run `hermes gateway`. " +
        "Your data stayed private — nothing was sent to the cloud.";
}
