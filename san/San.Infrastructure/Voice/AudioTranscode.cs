using System.Diagnostics;
using San.Application.Interfaces;

namespace San.Infrastructure.Voice;

// Browsers hand us whatever MediaRecorder supports — in practice audio/webm;codecs=opus
// on Chrome/Firefox and audio/mp4 (AAC) on Safari. llama.cpp decodes audio with
// miniaudio, which reads WAV, MP3 and FLAC and nothing else, so Opus and AAC have to
// be converted before Gemma can hear them. Whisper did its own decoding, which is why
// this step is new rather than something that went missing.
//
// ffmpeg is invoked as a process rather than linked as a library: it's one apt package
// in the image (see Dockerfile), and shelling out keeps the audio handling entirely
// outside the .NET process — a malformed upload crashes ffmpeg, not San.
public static class AudioTranscode
{
    // 16 kHz mono is what speech encoders want; anything more is bytes the model
    // discards. At 16-bit that's 32 KB per second of audio.
    private const int SampleRate = 16000;

    // A spoken message is seconds long. Five minutes is well past any legitimate use
    // and stops a stuck recorder from turning into a multi-megabyte base64 payload.
    private const int MaxSeconds = 300;

    // Wall-clock ceiling for the conversion itself. ffmpeg on a short clip finishes in
    // well under a second; anything near this is a hang, not slow work.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    // Returns 16 kHz mono 16-bit WAV bytes.
    //
    // Goes through temp FILES rather than piping both directions. Two reasons, both
    // learned the hard way with ffmpeg: the WebM/MP4 demuxers want a seekable input,
    // and a WAV written to a non-seekable pipe gets a header with a placeholder data
    // length, which strict decoders reject. Files make both problems disappear.
    public static async Task<byte[]> ToWavAsync(Stream input, string? sourceExtension, CancellationToken ct = default)
    {
        var dir = Path.GetTempPath();
        var stem = Path.Combine(dir, $"san-stt-{Guid.NewGuid():N}");
        var inPath = stem + (string.IsNullOrWhiteSpace(sourceExtension) ? ".bin" : sourceExtension);
        var outPath = stem + ".wav";

        try
        {
            await using (var f = File.Create(inPath))
                await input.CopyToAsync(f, ct);

            var psi = new ProcessStartInfo("ffmpeg")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[]
            {
                "-hide_banner", "-loglevel", "error",
                "-i", inPath,
                "-t", MaxSeconds.ToString(),
                "-ac", "1",                  // mono
                "-ar", SampleRate.ToString(),
                "-c:a", "pcm_s16le",
                "-y", outPath,
            }) psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi)
                ?? throw new SpeechToTextException(
                    "Couldn't start the audio converter (ffmpeg) — voice input needs it.",
                    "Process.Start returned null.");

            // Read stderr concurrently: ffmpeg writes diagnostics there, and a full
            // pipe buffer would deadlock a process we're simultaneously waiting on.
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            try
            {
                await proc.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TryKill(proc);
                throw new SpeechToTextException("Audio conversion timed out.", $"ffmpeg exceeded {Timeout.TotalSeconds:0}s.");
            }

            var stderr = await stderrTask;
            if (proc.ExitCode != 0)
                throw new SpeechToTextException(
                    "That recording couldn't be decoded. Try recording again.",
                    Short(stderr));

            var bytes = await File.ReadAllBytesAsync(outPath, ct);

            // A WAV header alone is 44 bytes. Anything at or below that decoded to
            // silence — a mic that never actually opened, which is worth saying
            // plainly instead of sending an empty clip to the model.
            if (bytes.Length <= 44)
                throw new SpeechToTextException(
                    "The recording came through empty — check that the microphone is on and permitted.",
                    $"Decoded to {bytes.Length} bytes.");

            return bytes;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // ffmpeg missing from PATH. Distinguished from a conversion failure because
            // the fix is completely different (rebuild the image vs. re-record).
            throw new SpeechToTextException(
                "Voice input needs ffmpeg, which isn't installed in this container. Rebuild the backend image.",
                ex.Message);
        }
        finally
        {
            TryDelete(inPath);
            TryDelete(outPath);
        }
    }

    // MediaRecorder's mime type, mapped to the extension ffmpeg uses to pick a demuxer.
    // Only a hint — ffmpeg probes the actual content and overrides a wrong guess.
    public static string ExtensionFor(string? contentType, string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "");
        if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 5) return ext;

        var mime = (contentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        return mime switch
        {
            "audio/webm" or "video/webm" => ".webm",
            "audio/mp4" or "video/mp4" or "audio/x-m4a" or "audio/aac" => ".m4a",
            "audio/ogg" or "application/ogg" => ".ogg",
            "audio/wav" or "audio/x-wav" or "audio/wave" => ".wav",
            "audio/mpeg" or "audio/mp3" => ".mp3",
            "audio/flac" or "audio/x-flac" => ".flac",
            _ => ".bin",
        };
    }

    private static void TryKill(Process p) { try { p.Kill(entireProcessTree: true); } catch { /* already gone */ } }
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { /* temp file, best effort */ } }
    private static string Short(string s) => string.IsNullOrWhiteSpace(s) ? "(no output)" : (s.Length <= 300 ? s.Trim() : s[..300].Trim());
}
