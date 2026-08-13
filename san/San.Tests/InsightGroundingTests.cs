using San.Application;

namespace San.Tests;

// Insights are uniquely dangerous to get wrong: they're written into NorthStar as
// durable facts, resurface in chat as things San "knows", and read with more authority
// than the raw data ever did. An improvised percentage is unfalsifiable after the fact.
//
// The tests come in pairs on purpose. A grounding check that rejects everything is as
// useless as one that accepts everything, and this one has already been over-tightened
// once — an earlier version harvested the parts of ISO dates as figures, which inflated
// the number pool until almost any percentage divided out of some pair and the ratio
// check silently stopped working.
public class InsightGroundingTests
{
    private const string Data = """
        week=2026-07-06 sleep_avg=6.4 spend=1200
        week=2026-07-13 sleep_avg=7.8 spend=800
        week=2026-07-20 sleep_avg=5.9 spend=1500
        """;

    private static bool Check(string title, string body, out string reason)
        => InsightGrounding.IsGrounded(new ProposedInsight(title, body), Data, out reason);

    [Fact]
    public void AcceptsFiguresThatAppearInTheData()
    {
        Assert.True(Check("Sleep and spending", "In the week you averaged 5.9 hours, you spent 1500.", out var r), r);
    }

    [Fact]
    public void AcceptsAPurelyQualitativeClaim()
        => Assert.True(Check("A pattern", "You spend more in weeks when you sleep less.", out var r), r);

    // The model may reason about shape freely; it may not introduce quantities.
    [Fact]
    public void RejectsAMoneyFigureThatIsNotInTheData()
    {
        Assert.False(Check("Spending", "You spent 4300 that week.", out var reason));
        Assert.Contains("4300", reason);
    }

    // The headline case: a confident, plausible, entirely invented percentage. 40% is
    // not within tolerance of any ratio this data supports.
    [Fact]
    public void RejectsAnInventedPercentage()
        => Assert.False(Check("Sleep vs spend", "Spending is up 40% in weeks you sleep under 6h.", out _));

    // Honest about what this check cannot do. 47% is accepted — not because the model
    // computed it, but because 1500 -> 800 is a 46.7% drop and 47% lands within
    // tolerance of it. With six figures in the table there are ~60 derivable ratios, so
    // a coincidental match is always possible and gets more likely as the data grows.
    //
    // Written as a passing test rather than filed as a bug because tightening it has a
    // worse failure mode: this guard runs unattended, and rejecting real insights loses
    // findings silently, while a coincidental accept still has to be a number the data
    // genuinely supports. Worth knowing before anyone reads a percentage here as proof
    // the model did arithmetic.
    [Fact]
    public void ACoincidentallyDerivablePercentageIsAccepted()
        => Assert.True(Check("Sleep vs spend", "Spending is up 47% in weeks you sleep under 6h.", out var r), r);

    // 1500 vs 800 is +87.5%, so a claim of 88% is honest reporting of a real ratio.
    [Fact]
    public void AcceptsAPercentageTheDataActuallySupports()
        => Assert.True(Check("Sleep vs spend", "Spending rose 88% between those weeks.", out var r), r);

    // Rounding is not invention: 87.5% reported as 87% must survive.
    [Fact]
    public void AcceptsReasonableRounding()
        => Assert.True(Check("Sleep vs spend", "Spending rose about 87% between those weeks.", out var r), r);

    // Prose counting, not a claim about the data — demanding "3" appear verbatim would
    // reject good insights for saying "3 of your weeks".
    [Fact]
    public void SmallCountingNumbersInProseAreAllowed()
        => Assert.True(Check("Overview", "Across 3 weeks, 2 patterns stand out.", out var r), r);

    // Dates are named, not asserted. Citing a real date from the data must not read as
    // citing the quantities 2026, 07 and 13.
    [Fact]
    public void CitingADateFromTheDataIsNotCitingNumbers()
        => Assert.True(Check("Best week", "The week of 2026-07-13 was your best for sleep.", out var r), r);

    // The other half of that: because dates are stripped from the DATA too, a year must
    // not become an available figure to cite as a quantity.
    [Fact]
    public void AYearFromADateCannotBeCitedAsAQuantity()
        => Assert.False(Check("Spending", "You spent 2026 that week.", out _));

    [Fact]
    public void GroundedInsightReportsNoReason()
    {
        Assert.True(Check("Fine", "Nothing numeric here at all.", out var reason));
        Assert.Equal("", reason);
    }

    // Nothing to check against — inventing a failure would block every insight drawn
    // from a sparse table.
    [Fact]
    public void SparseDataDoesNotManufactureARejection()
    {
        var insight = new ProposedInsight("Trend", "That is a 45% change.");
        Assert.True(InsightGrounding.IsGrounded(insight, "total=900", out var r), r);
    }
}
