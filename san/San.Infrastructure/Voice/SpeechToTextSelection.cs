namespace San.Infrastructure.Voice;

// Which STT engine San uses, resolved in one place so Program.cs and any diagnostics
// can never disagree about it.
public static class SpeechToTextSelection
{
    public const string Gemma = "gemma";
    public const string Whisper = "whisper";

    // STT_PROVIDER wins when set. Otherwise: Whisper if a Whisper URL is configured,
    // Gemma if not. That default is what makes removing the whisper container from
    // docker-compose sufficient to switch engines — the deployment that no longer runs
    // Whisper stops being asked to use it, while an existing deployment that still
    // points at one keeps the behaviour it had.
    public static string Resolve()
    {
        var explicitChoice = Environment.GetEnvironmentVariable("STT_PROVIDER");
        if (!string.IsNullOrWhiteSpace(explicitChoice))
            return explicitChoice.Trim().ToLowerInvariant() switch
            {
                "whisper" => Whisper,
                _ => Gemma,   // "gemma", "llamacpp", or a typo — the local model is the safer default now
            };

        return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WHISPER_SERVICE_URL"))
            ? Gemma
            : Whisper;
    }
}
