using System.Text.Json;
using Microsoft.Extensions.Logging;
using San.Application.Interfaces;

namespace San.Infrastructure.Voice;

// The original STT path: a self-hosted Whisper server speaking OpenAI's
// /v1/audio/transcriptions. Lifted out of VoiceController unchanged in behaviour so
// that switching engines is an env var, not a redeploy of different code — and so
// there's somewhere to fall back to if Gemma's transcription quality disappoints.
//
// Unlike GemmaTranscriber this needs no ffmpeg: Whisper decodes the browser's
// container itself.
//
// Config (env):
//   STT_PROVIDER=whisper
//   WHISPER_SERVICE_URL=http://whisper:8000
//   WHISPER_MODEL=whisper-1
public class WhisperTranscriber(IHttpClientFactory httpFactory, ILogger<WhisperTranscriber> logger) : ISpeechToText
{
    public string EngineName => "whisper";

    private static string? BaseUrl =>
        Environment.GetEnvironmentVariable("WHISPER_SERVICE_URL") is { Length: > 0 } v ? v.TrimEnd('/') : null;

    public bool IsConfigured => BaseUrl is not null;

    public async Task<string> TranscribeAsync(Stream audio, string? contentType, string? fileName, CancellationToken ct = default)
    {
        var baseUrl = BaseUrl
            ?? throw new SpeechToTextException("Speech-to-text isn't set up yet. Run a Whisper service and set WHISPER_SERVICE_URL, or switch STT_PROVIDER to gemma.");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var http = httpFactory.CreateClient("whisper");
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(audio);
        // Browsers send "audio/webm;codecs=opus". The MediaTypeHeaderValue CONSTRUCTOR
        // rejects any value carrying parameters (throws FormatException) — Parse accepts
        // them. Using the ctor here meant every real browser recording blew up before
        // Whisper was ever contacted, surfacing as a misleading "couldn't reach" error.
        fileContent.Headers.ContentType = ParseContentType(contentType);
        form.Add(fileContent, "file", string.IsNullOrWhiteSpace(fileName) ? "audio.webm" : fileName);
        form.Add(new StringContent(Environment.GetEnvironmentVariable("WHISPER_MODEL") ?? "whisper-1"), "model");

        HttpResponseMessage resp;
        try
        {
            resp = await http.PostAsync($"{baseUrl}/v1/audio/transcriptions", form, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't reach the Whisper service at {BaseUrl}", baseUrl);
            throw new SpeechToTextException("Couldn't reach the Whisper service.", ex.Message);
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Whisper returned HTTP {Status}: {Body}", (int)resp.StatusCode, Short(body));
            throw new SpeechToTextException($"Whisper service returned HTTP {(int)resp.StatusCode}.", Short(body));
        }

        // OpenAI-compatible response: { "text": "..." }. Fall back to the raw body.
        string? text = null;
        try { using var doc = JsonDocument.Parse(body); if (doc.RootElement.TryGetProperty("text", out var t)) text = t.GetString(); }
        catch (JsonException) { /* not JSON — treat whole body as text */ }

        var result = (text ?? body).Trim();
        logger.LogInformation("Whisper STT: {Chars} chars in {Ms}ms", result.Length, sw.ElapsedMilliseconds);
        return result;
    }

    // Never throws — falls back to a bare media type, then to audio/webm.
    private static System.Net.Http.Headers.MediaTypeHeaderValue ParseContentType(string? raw)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(raw, out var parsed))
                return parsed;
            var bare = raw.Split(';')[0].Trim();
            if (System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(bare, out var fallback))
                return fallback;
        }
        return new System.Net.Http.Headers.MediaTypeHeaderValue("audio/webm");
    }

    private static string Short(string s) => s.Length <= 300 ? s : s[..300];
}
