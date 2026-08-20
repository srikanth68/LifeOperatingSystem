using San.Application;

namespace San.Tests;

// The failure that matters here is not missing a settlement — that just leaves a
// reminder up. It is closing the WRONG one, which turns a nagging reminder into a
// missed payment. So the negative cases carry more weight than the positive ones.
public class SettlementsTests
{
    private static Settlement S(string vendor, string? what = null, decimal? amount = null)
        => new(vendor, what, amount);

    // Reminder texts as they actually exist in the live database.
    private static readonly string[] Open =
    [
        "Pay Spectrum Bill ($79.99)",
        "Spectrum bill of $65.98 is due on September 11th.",
        "Pay credit card bills",
        "Transfer money to Robinhood",
        "Shop for car insurance",
        "Schedule blood test",
        "Review Alcove Deductions Dispute Case: 15128 Scoter St",
    ];

    [Fact]
    public void ClosesEveryReminderForTheBillThatWasPaid()
    {
        var hits = Settlements.MatchesIn(S("Spectrum", "internet bill", 79.99m), Open, x => x);
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Contains("Spectrum", h, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    // A payment to one vendor must never retire an unrelated obligation.
    [InlineData("Spectrum", "Pay credit card bills")]
    [InlineData("Spectrum", "Transfer money to Robinhood")]
    [InlineData("Chase", "Pay Spectrum Bill ($79.99)")]
    [InlineData("Progressive", "Schedule blood test")]
    [InlineData("Robinhood", "Pay credit card bills")]
    public void NeverClosesSomethingElse(string vendor, string reminder)
        => Assert.Empty(Settlements.MatchesIn(S(vendor), [reminder], x => x));

    [Fact]
    public void APaymentToOneCardIssuerDoesNotCloseTheGenericCardReminder()
    {
        // "Pay credit card bills" names no issuer, so a Chase confirmation cannot prove
        // it is settled — the user may hold three cards. Leaving it up is the safe error.
        Assert.Empty(Settlements.MatchesIn(S("Chase", "credit card payment"),
                                           ["Pay credit card bills"], x => x));
    }

    [Fact]
    public void MatchesAnActionQueueItemToo()
    {
        var actions = new[] { "Check Alcove claims in Aasti", "Start your resume" };
        var hits = Settlements.MatchesIn(S("Alcove", "claims"), actions, x => x);
        Assert.Single(hits);
        Assert.Equal("Check Alcove claims in Aasti", hits[0]);
    }

    [Fact]
    public void SharingOnlyTheVendorNameIsNotEnough()
    {
        // "Alcove deductions dispute" and "Check Alcove claims" have one word in common.
        // Closing on that alone is the rule that would also let a Chase card payment
        // retire a Chase mortgage reminder, so the settlement has to describe the same
        // obligation, not merely name the same counterparty.
        Assert.Empty(Settlements.MatchesIn(S("Alcove", "deductions dispute"),
                                           ["Check Alcove claims in Aasti"], x => x));
    }

    [Fact]
    public void ParsesTheSettledArray()
    {
        var reply = """
        {"findings":[],"settled":[{"vendor":"Spectrum","what":"internet bill","amount":79.99}]}
        """;
        var s = Settlements.Parse(reply);
        Assert.Single(s);
        Assert.Equal("Spectrum", s[0].Vendor);
        Assert.Equal("internet bill", s[0].What);
        Assert.Equal(79.99m, s[0].Amount);
        Assert.Equal("Spectrum internet bill", s[0].Probe);
    }

    [Theory]
    // Every one of these means "nothing was settled". None may mean "close something".
    [InlineData("")]
    [InlineData("NOTHING_IMPORTANT")]
    [InlineData("{\"findings\":[]}")]
    [InlineData("{\"settled\":[]}")]
    [InlineData("{\"settled\":\"yes\"}")]
    [InlineData("{\"settled\":[{\"what\":\"a bill\"}]}")]
    [InlineData("not json at all")]
    public void SilenceAndGarbageBothMeanNothingWasSettled(string reply)
        => Assert.Empty(Settlements.Parse(reply));

    [Fact]
    public void SurvivesACodeFenceAroundTheJson()
    {
        var reply = "```json\n{\"settled\":[{\"vendor\":\"Spectrum\"}]}\n```";
        Assert.Single(Settlements.Parse(reply));
    }
}
