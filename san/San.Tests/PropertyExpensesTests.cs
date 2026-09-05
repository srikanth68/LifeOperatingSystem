using San.Application;

namespace San.Tests;

// Everything the model returns here ends up attached to money. A dropped proposal
// costs the user one manual assignment; an ungrounded one attaches a real transaction
// to the wrong property, or invents a link to nothing at all, and only surfaces at tax
// time. So the grounding tests matter more than the parsing ones.
public class PropertyExpensesTests
{
    private static readonly HashSet<string> RealTx = ["tx_1", "tx_2", "tx_3"];
    private static readonly HashSet<string> RealProps = ["prop_a", "prop_b"];

    private static List<ExpenseProposal> Ground(string reply) =>
        PropertyExpenses.Grounded(PropertyExpenses.Parse(reply), RealTx, RealProps);

    [Fact]
    public void ReadsAPlainArray()
    {
        var kept = Ground("""
        [{"transactionId":"tx_1","propertyId":"prop_a","category":"repair","reason":"Home Depot, plumbing"}]
        """);

        var p = Assert.Single(kept);
        Assert.Equal("tx_1", p.TransactionId);
        Assert.Equal("prop_a", p.PropertyId);
        Assert.Equal("repair", p.Category);
    }

    [Theory]
    [InlineData("""{"expenses":[{"transactionId":"tx_1","propertyId":"prop_a","category":"repair"}]}""")]
    [InlineData("""{"proposals":[{"transactionId":"tx_1","propertyId":"prop_a","category":"repair"}]}""")]
    [InlineData("```json\n[{\"transactionId\":\"tx_1\",\"propertyId\":\"prop_a\"}]\n```")]
    public void SurvivesTheEnvelopesASmallModelDriftsBetween(string reply)
        => Assert.Single(Ground(reply));

    [Fact]
    public void AnInventedTransactionIdIsDropped()
    {
        // The single most likely failure: the model writes a plausible-looking id that
        // was never in the batch. Attaching that to a property would create a ledger
        // entry pointing at nothing.
        Assert.Empty(Ground("""
        [{"transactionId":"tx_99","propertyId":"prop_a","category":"repair"}]
        """));
    }

    [Fact]
    public void AnInventedPropertyIdIsDropped()
        => Assert.Empty(Ground("""
        [{"transactionId":"tx_1","propertyId":"prop_zzz","category":"repair"}]
        """));

    [Fact]
    public void OneTransactionCannotBeClaimedByTwoProperties()
    {
        // Listing the same charge against two properties is not a split -- it is the
        // model guessing twice. A real split is something the user does by hand.
        var kept = Ground("""
        [{"transactionId":"tx_1","propertyId":"prop_a","category":"repair"},
         {"transactionId":"tx_1","propertyId":"prop_b","category":"repair"}]
        """);

        var p = Assert.Single(kept);
        Assert.Equal("prop_a", p.PropertyId);
    }

    [Fact]
    public void GoodProposalsSurviveAlongsideBadOnes()
    {
        // One real, one invented. Dropping the whole batch over a single bad row would
        // throw away work the model got right.
        var kept = Ground("""
        [{"transactionId":"tx_1","propertyId":"prop_a","category":"repair"},
         {"transactionId":"nope","propertyId":"prop_a","category":"repair"},
         {"transactionId":"tx_3","propertyId":"prop_b","category":"supplies"}]
        """);

        Assert.Equal(2, kept.Count);
        Assert.Equal(["tx_1", "tx_3"], kept.Select(k => k.TransactionId));
    }

    [Fact]
    public void AnUnknownCategoryBecomesOtherRatherThanARejection()
    {
        // Right property, vague category is still useful — the user fixes the category
        // when they confirm.
        var p = Assert.Single(Ground("""
        [{"transactionId":"tx_2","propertyId":"prop_b","category":"landscaping_and_yard"}]
        """));
        Assert.Equal("other", p.Category);
    }

    [Theory]
    // Every one of these means "nothing to propose". None may produce a link.
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("None of these look property related.")]
    [InlineData("not json at all")]
    [InlineData("""[{"propertyId":"prop_a"}]""")]
    [InlineData("""[{"transactionId":"tx_1"}]""")]
    [InlineData("""{"expenses":"none"}""")]
    public void SilenceAndGarbageBothProposeNothing(string reply)
        => Assert.Empty(Ground(reply));
}
