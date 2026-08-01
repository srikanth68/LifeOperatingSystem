using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace San.API.Controllers;

// Voice proxy — keeps San device-agnostic and the speech engines swappable, the
// same way LLM_PROVIDER keeps the model swappable. The browser records/plays audio;
// San forwards to whatever self-hosted, OpenAI-compatible speech services you point
// it at (Whisper for STT, Piper/openedai-speech for TTS). No cloud, fully local.
[ApiController, Route("api/voice")]
public partial class VoiceController(IHttpClientFactory httpFactory, ILogger<VoiceController> logger) : ControllerBase
{
    private static string? ServiceUrl(string key) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v.TrimEnd('/') : null;

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

    // Chat replies carry markdown and emoji meant for the screen, not a TTS engine —
    // read verbatim, Piper says "asterisk asterisk" and stumbles on symbols. Strip
    // formatting down to plain spoken text before it ever reaches the TTS service.
    private static string CleanForSpeech(string text)
    {
        text = FencedCodeRegex().Replace(text, " ");
        text = InlineCodeRegex().Replace(text, "$1");
        text = MarkdownLinkRegex().Replace(text, "$1");
        text = HeaderRegex().Replace(text, "");
        text = BlockquoteRegex().Replace(text, "");
        text = BulletRegex().Replace(text, "");
        text = NumberedListRegex().Replace(text, "");
        text = EmphasisRegex().Replace(text, "");
        text = EmojiRegex().Replace(text, "");
        text = WhitespaceRegex().Replace(text, " ");
        text = MultiNewlineRegex().Replace(text, ". ");
        text = NewlineRegex().Replace(text, ". ");
        return text.Trim();
    }

    [GeneratedRegex(@"```[\s\S]*?```")] private static partial Regex FencedCodeRegex();
    [GeneratedRegex(@"`([^`]+)`")] private static partial Regex InlineCodeRegex();
    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")] private static partial Regex MarkdownLinkRegex();
    [GeneratedRegex(@"^#{1,6}\s*", RegexOptions.Multiline)] private static partial Regex HeaderRegex();
    [GeneratedRegex(@"^>\s*", RegexOptions.Multiline)] private static partial Regex BlockquoteRegex();
    [GeneratedRegex(@"^[\-\*\+]\s+", RegexOptions.Multiline)] private static partial Regex BulletRegex();
    [GeneratedRegex(@"^\d+\.\s+", RegexOptions.Multiline)] private static partial Regex NumberedListRegex();
    [GeneratedRegex(@"\*\*\*|\*\*|\*|___|__|_")] private static partial Regex EmphasisRegex();
    [GeneratedRegex(@"\p{Cs}|\p{So}|️|‍")] private static partial Regex EmojiRegex();
    [GeneratedRegex(@"[ \t]+")] private static partial Regex WhitespaceRegex();
    [GeneratedRegex(@"\n{2,}")] private static partial Regex MultiNewlineRegex();
    [GeneratedRegex(@"\n")] private static partial Regex NewlineRegex();

    // Speech → text. Accepts the browser's recorded audio blob, forwards it to a
    // Whisper server's OpenAI-compatible /v1/audio/transcriptions endpoint.
    [HttpPost("transcribe")]
    [RequestSizeLimit(25_000_000)] // ~25 MB — plenty for a spoken message
    public async Task<IActionResult> Transcribe(IFormFile? audio)
    {
        var baseUrl = ServiceUrl("WHISPER_SERVICE_URL");
        if (baseUrl is null)
            return StatusCode(503, new { error = "Speech-to-text isn't set up yet. Run a Whisper service and set WHISPER_SERVICE_URL in san/.env." });
        if (audio is null || audio.Length == 0)
            return BadRequest(new { error = "No audio received." });

        try
        {
            var http = httpFactory.CreateClient("whisper");
            using var form = new MultipartFormDataContent();
            using var stream = audio.OpenReadStream();
            var fileContent = new StreamContent(stream);
            // Browsers send "audio/webm;codecs=opus". The MediaTypeHeaderValue CONSTRUCTOR
            // rejects any value carrying parameters (throws FormatException) — Parse accepts
            // them. Using the ctor here meant every real browser recording blew up before
            // Whisper was ever contacted, surfacing as a misleading "couldn't reach" error.
            fileContent.Headers.ContentType = ParseContentType(audio.ContentType);
            form.Add(fileContent, "file", string.IsNullOrWhiteSpace(audio.FileName) ? "audio.webm" : audio.FileName);
            form.Add(new StringContent(Environment.GetEnvironmentVariable("WHISPER_MODEL") ?? "whisper-1"), "model");

            using var resp = await http.PostAsync($"{baseUrl}/v1/audio/transcriptions", form);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Whisper returned HTTP {Status}: {Body}", (int)resp.StatusCode, Trim(body));
                return StatusCode(502, new { error = $"Whisper service returned HTTP {(int)resp.StatusCode}.", detail = Trim(body) });
            }

            // OpenAI-compatible response: { "text": "..." }. Fall back to raw body.
            string? text = null;
            try { using var doc = JsonDocument.Parse(body); if (doc.RootElement.TryGetProperty("text", out var t)) text = t.GetString(); }
            catch { /* not JSON — treat whole body as text */ }
            return Ok(new { text = (text ?? body).Trim() });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't reach the Whisper service at {BaseUrl}", baseUrl);
            return StatusCode(502, new { error = "Couldn't reach the Whisper service.", detail = ex.Message });
        }
    }

    // Text → speech. Forwards to Piper (via openedai-speech's OpenAI-compatible
    // /v1/audio/speech) and streams the audio back for the browser to play.
    [HttpPost("speak")]
    public async Task<IActionResult> Speak([FromBody] SpeakRequest req)
    {
        // TTS_* is the engine-agnostic name (Kokoro, Piper, anything OpenAI-compatible);
        // PIPER_* stays supported so existing deployments keep working.
        var baseUrl = ServiceUrl("TTS_SERVICE_URL") ?? ServiceUrl("PIPER_SERVICE_URL");
        if (baseUrl is null)
            return StatusCode(503, new { error = "Text-to-speech isn't set up yet. Start a TTS service and set TTS_SERVICE_URL in san/.env." });
        if (string.IsNullOrWhiteSpace(req.Text))
            return BadRequest(new { error = "No text to speak." });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var http = httpFactory.CreateClient("piper");
            var payload = new
            {
                model = Environment.GetEnvironmentVariable("TTS_MODEL")
                        ?? Environment.GetEnvironmentVariable("PIPER_MODEL") ?? "tts-1",
                input = CleanForSpeech(req.Text),
                voice = string.IsNullOrWhiteSpace(req.Voice)
                    ? Environment.GetEnvironmentVariable("TTS_VOICE")
                      ?? Environment.GetEnvironmentVariable("PIPER_VOICE") ?? "alloy"
                    : req.Voice,
                response_format = "mp3",
            };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync($"{baseUrl}/v1/audio/speech", content);
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = Trim(await resp.Content.ReadAsStringAsync());
                logger.LogWarning("Piper returned HTTP {Status}: {Body}", (int)resp.StatusCode, errBody);
                return StatusCode(502, new { error = $"TTS service returned HTTP {(int)resp.StatusCode}.", detail = errBody });
            }

            var audioBytes = await resp.Content.ReadAsByteArrayAsync();
            var contentType = resp.Content.Headers.ContentType?.ToString() ?? "audio/mpeg";
            logger.LogInformation("TTS synthesis took {Ms}ms for {Chars} chars ({Bytes} bytes audio)",
                sw.ElapsedMilliseconds, req.Text.Length, audioBytes.Length);
            return File(audioBytes, contentType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't reach the TTS service at {BaseUrl}", baseUrl);
            return StatusCode(502, new { error = "Couldn't reach the TTS service.", detail = ex.Message });
        }
    }

    // Lets the frontend show/hide the mic + speaker buttons based on what's configured.
    [HttpGet("status")]
    public IActionResult Status() => Ok(new
    {
        sttReady = ServiceUrl("WHISPER_SERVICE_URL") is not null,
        ttsReady = (ServiceUrl("TTS_SERVICE_URL") ?? ServiceUrl("PIPER_SERVICE_URL")) is not null,
    });

    private static string Trim(string s) => s.Length <= 300 ? s : s[..300];
}

public record SpeakRequest(string Text, string? Voice);
