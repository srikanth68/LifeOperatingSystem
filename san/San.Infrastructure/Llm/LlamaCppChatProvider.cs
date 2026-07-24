using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using San.Application.Interfaces;

namespace San.Infrastructure.Llm;

// Local, private LLM via llama.cpp's server (or any OpenAI-compatible endpoint).
// llama.cpp exposes POST {base}/v1/chat/completions with the OpenAI schema —
// which is different from Ollama's native /api/chat (see OllamaChatProvider).
// Keeps all data on-device: nothing leaves the machine / VPN tunnel.
//
// Config (env):
//   LLM_PROVIDER=llamacpp
//   LLM_BASE_URL=http://<host>:<port>   (e.g. http://localhost:8080 or the VPN-tunnel URL)
//   LLM_MODEL=gemma-4                   (llama.cpp usually ignores this, but it's echoed for clarity)
//   LLM_API_KEY=...                     (optional — only if you started the server with --api-key)
public class LlamaCppChatProvider(HttpClient http, IConfiguration config) : IChatProvider
{
    public string ProviderName => "llamacpp";
    public string ModelName => config["Llm:Model"] ?? "gemma-4";

    private static string BaseUrl =>
        (Environment.GetEnvironmentVariable("LLM_BASE_URL")
         ?? Environment.GetEnvironmentVariable("LLAMACPP_BASE_URL")
         ?? "http://localhost:8080").TrimEnd('/');

    public async Task<string> CompleteAsync(string systemPrompt, List<ChatTurn> history, CancellationToken ct = default)
    {
        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        messages.AddRange(history.Select(h => (object)new { role = h.Role, content = h.Content }));

        var payload = new
        {
            model = ModelName,
            messages,
            stream = false,
            temperature = 0.7,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/chat/completions");
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct);
        }
        catch
        {
            // Local model unreachable (Meshnet box asleep/offline). Stay local — never
            // fall back to a cloud LLM, since the prompt carries private module data.
            return Offline;
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            return $"⚠️ San's local model returned an error ({(int)resp.StatusCode}). Nothing left your machine — try again in a moment.";

        try
        {
            var reply = JsonDocument.Parse(body).RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return string.IsNullOrWhiteSpace(reply)
                ? "🤔 The local model returned an empty answer. Try rephrasing, or check the model in Settings."
                : reply;
        }
        catch
        {
            return "⚠️ San got an unexpected response from the local model. Nothing left your machine.";
        }
    }

    private const string Offline =
        "🔌 San's local model is offline right now — the machine hosting Gemma looks asleep or off the Meshnet. " +
        "Your data stayed private (nothing was sent to the cloud). Wake that machine and try again.";
}
