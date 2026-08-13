using San.Infrastructure.Voice;

namespace San.Tests;

// A chat model asked to transcribe will cheerfully answer the question it just heard,
// or narrate what it did, instead of writing down the words. The system prompt does
// most of the work; Clean is the backstop for when it doesn't. The risk in a backstop
// like this is over-reach — stripping something the user actually said — so roughly
// half of these guard the other direction.
public class GemmaTranscriberTests
{
    [Theory]
    [InlineData("Here is the transcription: remind me to call Sarah", "remind me to call Sarah")]
    [InlineData("Transcript: what is my agenda today", "what is my agenda today")]
    [InlineData("The speaker says: log a workout", "log a workout")]
    [InlineData("Here's the transcript: pay the electricity bill", "pay the electricity bill")]
    public void StripsNarratingOpeners(string raw, string expected)
        => Assert.Equal(expected, GemmaTranscriber.Clean(raw));

    // Real speech, left exactly as spoken. The third case is the sharp one: it contains
    // a colon early on, which is what the preamble pattern keys off.
    [Theory]
    [InlineData("remind me to call Sarah at four")]
    [InlineData("Text me when you get home and let me know")]
    [InlineData("What did I spend on groceries: last month")]
    [InlineData("add a workout for tomorrow morning")]
    public void LeavesOrdinarySpeechUntouched(string raw)
        => Assert.Equal(raw, GemmaTranscriber.Clean(raw));

    // Stripping must never empty the result — someone who said only "Transcript:"
    // should get those words back, not silence.
    [Fact]
    public void DoesNotStripWhenNothingWouldRemain()
        => Assert.Equal("Transcript:", GemmaTranscriber.Clean("Transcript:"));

    [Theory]
    [InlineData("(no speech)")]
    [InlineData("No speech.")]
    [InlineData("[No Speech]")]
    [InlineData("*no speech*")]
    public void NoSpeechSentinelBecomesEmpty(string raw)
        => Assert.Equal("", GemmaTranscriber.Clean(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInputIsEmptyOutput(string? raw)
        => Assert.Equal("", GemmaTranscriber.Clean(raw));

    [Fact]
    public void UnwrapsQuotesAroundTheWholeTranscript()
        => Assert.Equal("book a table for two", GemmaTranscriber.Clean("\"book a table for two\""));

    // A transcript that quotes someone must keep its punctuation — the wrapping-quote
    // strip only applies when the quotes wrap everything and nothing else is quoted.
    [Fact]
    public void KeepsInternalQuotes()
    {
        const string spoken = "she said \"call me back\" and hung up";
        Assert.Equal(spoken, GemmaTranscriber.Clean(spoken));
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
        => Assert.Equal("hello there", GemmaTranscriber.Clean("  hello there\n"));
}
