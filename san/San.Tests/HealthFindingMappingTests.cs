using San.Application;
using San.Application.DTOs;

namespace San.Tests;

// Vitara concludes; San decides whether to say it. This is the seam between them, and
// the two things that can go wrong here are both silent: a key that changes shape
// stops deduplicating, and a severity that maps too high turns a ring into an alarm.
public class HealthFindingMappingTests
{
    private static HealthFinding F(string severity = "info", int days = 1, string key = "deviation:resting_hr:high") =>
        new(key, "deviation", severity, "Resting heart rate has been high against your baseline for 2 days.", days);

    [Theory]
    [InlineData("high", "high")]
    [InlineData("notable", "medium")]
    [InlineData("info", "low")]
    [InlineData("", "low")]
    public void SeverityMapsOntoTheLedgerVocabulary(string vitara, string expected)
        => Assert.Equal(expected, HealthFindingMapping.Severity(vitara));

    [Fact]
    public void NothingIsEverCritical()
    {
        // A wearable is not equipped to declare an emergency. Whatever Vitara says,
        // the ledger must never receive the severity reserved for one.
        foreach (var s in new[] { "high", "notable", "info", "critical", "anything" })
            Assert.NotEqual("critical", HealthFindingMapping.Severity(s));
    }

    [Fact]
    public void TheKeyCrossesUnchanged()
    {
        // The whole deduplication chain hangs off this. Vitara derives the key in code
        // so it is identical run to run; decorating it here would produce one
        // notification every morning forever.
        var finding = F(key: "early_illness:composite:high");
        Assert.Equal("early_illness:composite:high", HealthFindingMapping.ToAgentFinding(finding).Key);
    }

    [Fact]
    public void ASingleDayCarriesNoDayCount()
    {
        // The summary is passed through untouched. Appending "(day 1)" to something
        // detected this morning is noise dressed as precision.
        var finding = F(days: 1);
        Assert.Equal(finding.Summary, HealthFindingMapping.Message(finding));
    }

    [Fact]
    public void AContinuingFindingSaysHowLong()
    {
        // The difference between "your resting heart rate is up" and something worth
        // acting on.
        Assert.Contains("(day 5)", HealthFindingMapping.Message(F(days: 5)));
    }
}
