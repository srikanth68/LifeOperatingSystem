using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using NorthStar.Application.Interfaces;

namespace NorthStar.Infrastructure.Embeddings;

// An OpenAI-compatible /v1/embeddings endpoint on the user's own machine -- llama.cpp's
// server started with --embeddings, Ollama, LM Studio. Configured by environment:
//
//   EMBEDDINGS_URL            base URL or the full /v1/embeddings URL; unset = off
//   EMBEDDINGS_MODEL          model name sent with the request (and what the stored
//                             vectors are tagged with)
//   EMBEDDINGS_QUERY_PREFIX   e.g. "search_query: " for nomic-embed-text
//   EMBEDDINGS_DOC_PREFIX     e.g. "search_document: "
//
// Every failure returns null and is logged once per minute at most: a stopped model
// server must cost recall its meaning-search, never the recall itself.
public class LocalEmbedder(HttpClient http, ILogger<LocalEmbedder> logger) : IEmbedder
{
    private static readonly string? Url = Normalise(Environment.GetEnvironmentVariable("EMBEDDINGS_URL"));
    private static readonly string QueryPrefix = Environment.GetEnvironmentVariable("EMBEDDINGS_QUERY_PREFIX") ?? "";
    private static readonly string DocPrefix = Environment.GetEnvironmentVariable("EMBEDDINGS_DOC_PREFIX") ?? "";
    private DateTime lastWarned = DateTime.MinValue;

    public bool Enabled => Url is not null;
    public string Model { get; } = Environment.GetEnvironmentVariable("EMBEDDINGS_MODEL") ?? "local";

    public async Task<float[][]?> EmbedAsync(IReadOnlyList<string> texts, bool isQuery, CancellationToken ct = default)
    {
        if (Url is null || texts.Count == 0) return null;
        var prefix = isQuery ? QueryPrefix : DocPrefix;
        try
        {
            using var resp = await http.PostAsJsonAsync(Url,
                new { model = Model, input = texts.Select(t => prefix + t).ToArray() }, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Warn($"HTTP {(int)resp.StatusCode}");
                return null;
            }
            var body = await resp.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct);
            var data = body?.Data?.OrderBy(d => d.Index).Select(d => d.Embedding ?? []).ToArray();
            return data is not null && data.Length == texts.Count && data.All(v => v.Length > 0) ? data : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            if (!ct.IsCancellationRequested) Warn(ex.Message);
            return null;
        }
    }

    private void Warn(string why)
    {
        if (DateTime.UtcNow - lastWarned < TimeSpan.FromMinutes(1)) return;
        lastWarned = DateTime.UtcNow;
        logger.LogWarning("Embeddings unavailable ({Why}) at {Url}; recall is using keywords and entities only.", why, Url);
    }

    private static string? Normalise(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim().TrimEnd('/');
        return url.EndsWith("/embeddings", StringComparison.OrdinalIgnoreCase) ? url : url + "/v1/embeddings";
    }

    private sealed record EmbeddingResponse([property: JsonPropertyName("data")] List<EmbeddingItem>? Data);
    private sealed record EmbeddingItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
