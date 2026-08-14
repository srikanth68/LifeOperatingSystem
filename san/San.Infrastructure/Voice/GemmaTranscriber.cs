using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using San.Application.Interfaces;

namespace San.Infrastructure.Voice;

// Speech → text using the multimodal model San already runs, instead of a dedicated
// speech-recognition service.
//
// Gemma 4 is natively multimodal — llama-server started with `-hf unsloth/gemma-4-*`
// pulls the projector automatically and reports `"audio": true` on /props, so audio
// rides in on the same /v1/chat/completions endpoint the chat loop already uses, as
// an `input_audio` content part. One less service, one less model resident in RAM,
// and one less thing that can be down while the rest of San is up.
//
// The trade: a chat model asked to transcribe can decide to be helpful and ANSWER the
// question it just heard, or narrate ("The speaker says..."). Everything below —
// the system prompt, temperature 0, thinking off, and the cleanup pass — exists to
// keep it doing the boring literal thing.
//
// Config (env):
//   LLM_BASE_URL=http://host.docker.internal:8080   (shared with the chat provider)
//   STT_MODEL=gemma-4                               (echoed; llama.cpp ignores it)
public partial class GemmaTranscriber(IHttpClientFactory httpFactory, ILogger<GemmaTranscriber> logger) : ISpeechToText
{
    public string EngineName => "gemma";

    // The chat provider's URL is the same server, so a working San chat implies a
    // working transcriber — there is no separate thing left to misconfigure.
    private static string? BaseUrl =>
        (Environment.GetEnvironmentVariable("LLM_BASE_URL")
         ?? Environment.GetEnvironmentVariable("LLAMACPP_BASE_URL"))?.TrimEnd('/');

    public bool IsConfigured => BaseUrl is not null;

    // Slot 1: kept away from the chat slot so the two never evict each other. Falls back
    // to 0 on a single-slot server, where there is nothing to separate.
    private static int Slot =>
        int.TryParse(Environment.GetEnvironmentVariable("STT_SLOT"), out var s) && s >= 0 ? s : 1;

    // Written as a machine spec rather than a polite request. "Output ONLY" and the
    // explicit no-answering clause are both load-bearing: without them the model
    // replies to questions in the audio instead of writing them down.
    private const string SystemPrompt =
        "You are a speech transcription engine, not an assistant.\n" +
        "Output ONLY the exact words spoken in the audio, verbatim.\n" +
        "Do not answer questions in the audio. Do not translate. Do not summarise.\n" +
        "Do not add quotation marks, speaker labels, timestamps or commentary.\n" +
        "If there is no intelligible speech, output exactly: (no speech)";

    public async Task<string> TranscribeAsync(Stream audio, string? contentType, string? fileName, CancellationToken ct = default)
    {
        var baseUrl = BaseUrl
            ?? throw new SpeechToTextException("Speech-to-text isn't set up — LLM_BASE_URL isn't configured, so there's no model to hear the audio.");

        var wav = await AudioTranscode.ToWavAsync(audio, AudioTranscode.ExtensionFor(contentType, fileName), ct);
        var seconds = (wav.Length - 44) / 32000.0; // 16 kHz mono 16-bit, minus the header

        var payload = new Dictionary<string, object>
        {
            ["model"] = Environment.GetEnvironmentVariable("STT_MODEL") ?? "gemma-4",
            ["messages"] = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_audio", input_audio = new { data = Convert.ToBase64String(wav), format = "wav" } },
                        new { type = "text", text = "Transcribe the audio." },
                    },
                },
            },
            ["stream"] = false,
            // Transcription has one right answer; sampling can only invent words that
            // were never said.
            ["temperature"] = 0,
            // Same 15x cost the chat provider documents — and here the deliberation is
            // pure waste, since there is no decision to make.
            ["chat_template_kwargs"] = new Dictionary<string, object> { ["enable_thinking"] = false },
            // Generous relative to speech: ~1 token per word, and the cap only exists
            // to bound a model that starts rambling instead of transcribing.
            ["max_tokens"] = 800,
            // A different slot from chat, on purpose. Both hit the same llama-server, and
            // the prompt cache is per slot — sharing one would mean every voice turn's
            // transcription evicted the chat prefix and vice versa, so neither would ever
            // hit warm. Audio prompts have no reusable prefix anyway, so this slot is
            // effectively scratch space.
            ["id_slot"] = Slot,
        };

        var http = httpFactory.CreateClient("gemma-stt");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct);
        }
        catch (Exception ex)
        {
            // Deliberately mirrors the chat provider's offline message: it is the same
            // machine being asleep, and the user should recognise the situation.
            throw new SpeechToTextException(
                "San's local model is offline, so it couldn't hear that. Your audio stayed on your machine.",
                ex.Message);
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Gemma STT returned HTTP {Status}: {Body}", (int)resp.StatusCode, Short(body));
            // 400 here almost always means the server was built or started without a
            // multimodal projector, so it has no idea what an audio part is.
            var hint = (int)resp.StatusCode == 400
                ? " The model server may have started without audio support — check /props reports \"audio\": true."
                : "";
            throw new SpeechToTextException($"The local model returned HTTP {(int)resp.StatusCode} for that audio.{hint}", Short(body));
        }

        string? text;
        try
        {
            using var doc = JsonDocument.Parse(body);
            text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException)
        {
            throw new SpeechToTextException("The local model sent back a response San couldn't read.", Short(body));
        }

        var cleaned = Clean(text);
        logger.LogInformation("Gemma STT: {Seconds:0.0}s audio → {Chars} chars in {Ms}ms",
            seconds, cleaned.Length, sw.ElapsedMilliseconds);
        return cleaned;
    }

    // Belt and braces on top of the system prompt. Each rule here corresponds to a
    // way an instruction-following model bends a literal instruction — none of them
    // can damage a genuinely clean transcript, which is why they run unconditionally.
    internal static string Clean(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0) return "";

        // The no-speech sentinel, however the model chose to punctuate it.
        if (NoSpeechRegex().IsMatch(text)) return "";

        // Strip a narrating opener ("Here is the transcription:"), but only when doing
        // so leaves something behind. Without that guard, someone who actually said
        // "Transcript:" and nothing else would get an empty transcription — and a
        // spoken sentence that merely happens to contain an early colon would lose its
        // opening words.
        var preamble = PreambleRegex().Match(text);
        if (preamble.Success && preamble.Length < 40)
        {
            var stripped = text[preamble.Length..].Trim();
            if (stripped.Length > 0) text = stripped;
        }

        // Strip a wrapping quote pair only when it wraps the WHOLE string — otherwise
        // a transcript that legitimately quotes someone would lose its punctuation.
        if (text.Length >= 2 &&
            ((text[0] == '"' && text[^1] == '"') ||
             (text[0] == '“' && text[^1] == '”') ||
             (text[0] == '\'' && text[^1] == '\'')) &&
            text[1..^1].IndexOfAny(['"', '“', '”']) < 0)
            text = text[1..^1].Trim();

        return text;
    }

    [GeneratedRegex(@"^[\(\[\*""']*\s*no\s+speech\s*[\)\]\*""'\.]*$", RegexOptions.IgnoreCase)]
    private static partial Regex NoSpeechRegex();

    // Only the narrating openers, anchored and followed by a colon — a transcript is
    // very unlikely to genuinely begin this way, and requiring the colon keeps a
    // sentence that merely starts with "The speaker" intact.
    [GeneratedRegex(@"^(here is|here's|this is)?\s*(the\s+)?(verbatim\s+)?(transcription|transcript|audio|speaker|text)[^:\n]{0,20}:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex PreambleRegex();

    private static string Short(string s) => string.IsNullOrWhiteSpace(s) ? "(empty)" : (s.Length <= 300 ? s.Trim() : s[..300].Trim());
}
