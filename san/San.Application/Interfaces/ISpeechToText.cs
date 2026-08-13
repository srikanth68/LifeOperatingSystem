namespace San.Application.Interfaces;

// Speech → text, behind an interface for the same reason IChatProvider exists: the
// engine is a deployment choice, not a code one. One implementation ships —
// GemmaTranscriber, which uses the multimodal model San already runs, so no separate
// speech service exists to go down, cold-start, or hold a second model in RAM.
public interface ISpeechToText
{
    // Reported on /api/voice/status and in logs — the quickest confirmation that a
    // deployment is running the build you think it is.
    string EngineName { get; }

    // False when the engine's URL isn't configured — the controller turns this into
    // a 503 with setup instructions rather than a failed call.
    bool IsConfigured { get; }

    // Returns the spoken words, or an empty string when the audio held no speech
    // (silence, a mis-click, a mic that never opened). Empty is a normal outcome,
    // not an error — the caller decides whether to show anything.
    // Throws SpeechToTextException when the engine is reachable but unhappy.
    Task<string> TranscribeAsync(Stream audio, string? contentType, string? fileName, CancellationToken ct = default);
}

// Carries a message already written for the user, so the controller can surface it
// without inventing its own wording (the same contract LlamaCppAgentChatProvider's
// LlmHttpException uses).
public class SpeechToTextException(string userMessage, string? detail = null) : Exception(userMessage)
{
    public string UserMessage => Message;
    public string? Detail { get; } = detail;
}
