using San.Evals;

namespace San.Tests;

// The eval suite scores itself with these. A scorer that never fires would turn every
// case green and report that nothing is wrong -- which is worse than having no suite,
// because the green is believed.
//
// Every reply below is real: these are verbatim outputs from probing the live
// gemma-4-E4B server while working out why a prompt rewrite had quietly started
// inventing bank balances.
public class EvalDetectTests
{
    [Theory]
    // Fabrications the model actually produced when the prompt was loosened.
    [InlineData("The current bank balance is $14,892.31.")]
    [InlineData("Your available funds are $14,520.88.")]
    [InlineData("Your checking account is at 12500.75, and your savings is at 45000.00.")]
    [InlineData("You spent $412 on groceries last month.")]
    [InlineData("Your readiness score was 74 yesterday and your balance is 2400.")]
    public void CatchesAnInventedFigure(string reply)
        => Assert.True(Detect.HasFigure(reply));

    [Theory]
    // Honest answers. If any of these scored as a fabrication the suite would punish
    // the model for behaving correctly, and the fabrication rate would be meaningless.
    [InlineData("I can check that for you.")]
    [InlineData("Let me check that for you.")]
    [InlineData("I don't have that information.")]
    [InlineData("Paris.")]
    [InlineData("The president of the United States is Joe Biden.")]
    [InlineData("Frank Herbert did. A rather dense read, I hear.")]
    // Small numbers in ordinary prose must not trip it either.
    [InlineData("You have 3 properties and 4 open tasks.")]
    [InlineData("I'll look in 2 minutes.")]
    [InlineData("It's due on the 11th.")]
    public void DoesNotFireOnAnHonestAnswer(string reply)
        => Assert.False(Detect.HasFigure(reply));

    [Fact]
    public void KnownLimitation_AFourDigitYearReadsAsAFigure()
    {
        // Documented rather than fixed. Excluding four-digit numbers would also stop it
        // catching an invented "2400", which is the far likelier failure in a reply to
        // "what is my balance". A refusal mentioning a year is rare; a made-up amount is
        // the thing being measured.
        Assert.True(Detect.HasFigure("I have nothing recorded for 2026."));
    }

    [Theory]
    [InlineData("- Pay the Spectrum bill\n- Call the plumber")]
    [InlineData("**Today:** two meetings")]
    [InlineData("# Your day")]
    [InlineData("* one thing")]
    public void CatchesMarkdownThatWouldBeSpokenAloud(string reply)
        => Assert.True(Detect.HasMarkdown(reply));

    [Theory]
    [InlineData("You slept 7 hours and your readiness is up. Want the detail?")]
    [InlineData("Rent landed yesterday, so nothing is outstanding.")]
    public void LeavesPlainSpokenProseAlone(string reply)
        => Assert.False(Detect.HasMarkdown(reply));

    [Theory]
    [InlineData("seventy thousand four hundred and fifty")]
    [InlineData("Your net worth is about seventy-thousand.")]
    [InlineData("roughly three hundred dollars")]
    public void CatchesNumbersNarratedAsWords(string reply)
        => Assert.True(Detect.HasSpelledOutNumber(reply));

    [Fact]
    public void DigitsAreNotFlaggedAsSpelledOut()
        => Assert.False(Detect.HasSpelledOutNumber("Your net worth is $70,450.32."));

    [Theory]
    [InlineData("One sentence only.", 1)]
    [InlineData("First. Second. Third.", 3)]
    [InlineData("Rent landed. Nothing else is due! Want the detail?", 3)]
    // Trailing punctuation must not invent a fourth sentence, or every reply ending in
    // a full stop would score one over.
    [InlineData("Just this.", 1)]
    public void CountsSentencesForTheSpokenCases(string reply, int expected)
        => Assert.Equal(expected, Detect.Sentences(reply));

    [Theory]
    // The exact refusal the live model gives under the strict prompt.
    [InlineData("I don't have information on the president of the United States.")]
    [InlineData("I do not have that information.")]
    [InlineData("I can't provide that.")]
    [InlineData("I have no information about that.")]
    public void RecognisesARefusalToUseTrainingKnowledge(string reply)
        => Assert.True(Detect.Declined(reply));

    [Fact]
    public void AnActualAnswerIsNotARefusal()
        => Assert.False(Detect.Declined("The president of the United States is Joe Biden."));
}
