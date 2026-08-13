using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using San.Application.Interfaces;

namespace San.API.Controllers;

// Voice proxy — keeps San device-agnostic and the speech engines swappable, the
// same way LLM_PROVIDER keeps the model swappable. The browser records/plays audio;
// San forwards to whatever self-hosted, OpenAI-compatible speech services you point
// it at. No cloud, fully local.
//
// STT lives behind ISpeechToText, which Gemma answers using its native audio input.
// TTS is still a direct proxy — Gemma generates no audio, so a speech engine
// remains a genuinely separate service there.
[ApiController, Route("api/voice")]
public partial class VoiceController(
    IHttpClientFactory httpFactory,
    ISpeechToText stt,
    ILogger<VoiceController> logger) : ControllerBase
{
    private static string? ServiceUrl(string key) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v.TrimEnd('/') : null;

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

    // Speech → text. Accepts the browser's recorded audio blob. The response shape is
    // unchanged from when this proxied to a speech service, so no client needs updating.
    [HttpPost("transcribe")]
    [RequestSizeLimit(25_000_000)] // ~25 MB — plenty for a spoken message
    public async Task<IActionResult> Transcribe(IFormFile? audio, CancellationToken ct)
    {
        if (!stt.IsConfigured)
            return StatusCode(503, new { error = "Speech-to-text isn't set up yet — LLM_BASE_URL isn't configured, so there's no model to hear the audio." });
        if (audio is null || audio.Length == 0)
            return BadRequest(new { error = "No audio received." });

        try
        {
            using var stream = audio.OpenReadStream();
            var text = await stt.TranscribeAsync(stream, audio.ContentType, audio.FileName, ct);
            return Ok(new { text, engine = stt.EngineName });
        }
        catch (SpeechToTextException ex)
        {
            // The engine already phrased this for the user; 502 keeps the existing
            // client-side handling for "the speech service is unhappy".
            logger.LogWarning("Transcription failed via {Engine}: {Message} — {Detail}", stt.EngineName, ex.UserMessage, ex.Detail);
            return StatusCode(502, new { error = ex.UserMessage, detail = ex.Detail });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Browser navigated away or aborted the fetch — not a failure worth logging
            // as one. 499 is nginx's "client closed request".
            return StatusCode(499);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected transcription failure via {Engine}", stt.EngineName);
            return StatusCode(502, new { error = "Transcription failed unexpectedly.", detail = ex.Message });
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
        sttReady = stt.IsConfigured,
        // Names the engine that would answer. Cheap, and it's the quickest way to
        // confirm a deployment is actually running the build you think it is.
        sttEngine = stt.EngineName,
        ttsReady = (ServiceUrl("TTS_SERVICE_URL") ?? ServiceUrl("PIPER_SERVICE_URL")) is not null,
    });

    private static string Trim(string s) => s.Length <= 300 ? s : s[..300];
}

public record SpeakRequest(string Text, string? Voice);
