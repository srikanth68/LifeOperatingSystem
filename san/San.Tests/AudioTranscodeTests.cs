using San.Infrastructure.Voice;

namespace San.Tests;

// Only the pure part is covered here: ToWavAsync shells out to ffmpeg, which belongs in
// an integration test with a real binary and a real recording. The extension mapping is
// what decides which demuxer ffmpeg reaches for first, and it runs on every voice note.
public class AudioTranscodeTests
{
    // What Chrome and Firefox actually send, parameters and all — MediaRecorder reports
    // "audio/webm;codecs=opus", and a mapper that only matched the bare type would fall
    // through to .bin for every desktop recording.
    [Theory]
    [InlineData("audio/webm;codecs=opus", ".webm")]
    [InlineData("audio/webm", ".webm")]
    [InlineData("audio/mp4", ".m4a")]          // Safari
    [InlineData("audio/ogg", ".ogg")]
    [InlineData("audio/wav", ".wav")]
    [InlineData("audio/mpeg", ".mp3")]
    [InlineData("audio/flac", ".flac")]
    public void MapsBrowserContentTypes(string contentType, string expected)
        => Assert.Equal(expected, AudioTranscode.ExtensionFor(contentType, null));

    [Fact]
    public void IsCaseInsensitive()
        => Assert.Equal(".webm", AudioTranscode.ExtensionFor("AUDIO/WEBM", null));

    // A filename is the stronger signal when present: the browser's own blob type is a
    // guess about the container, while the name usually came from a real file.
    [Fact]
    public void FilenameExtensionWinsOverContentType()
        => Assert.Equal(".m4a", AudioTranscode.ExtensionFor("audio/webm", "note.m4a"));

    [Fact]
    public void UnknownTypeFallsBackToBin()
        => Assert.Equal(".bin", AudioTranscode.ExtensionFor("application/octet-stream", null));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void MissingEverythingStillReturnsSomething(string? ct, string? name)
        => Assert.Equal(".bin", AudioTranscode.ExtensionFor(ct, name));
}
