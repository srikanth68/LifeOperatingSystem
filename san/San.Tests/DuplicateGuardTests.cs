using San.Application;

namespace San.Tests;

// The duplicate pairs are verbatim from the live database, where three "Spectrum Bill
// Due" alerts and a daily habit nudge had accumulated. The distinct pairs are the ones
// that must survive: obligations that share a verb but are not the same thing.
public class DuplicateGuardTests
{
    private static readonly DateTime Base = new(2026, 8, 30, 13, 0, 0, DateTimeKind.Utc);

    [Theory]
    // The three Spectrum alerts, worded differently by the model on three separate runs.
    [InlineData("Pay Spectrum Bill ($79.99)", "Spectrum bill ($79.99) is due on August 30th.")]
    [InlineData("Spectrum bill of $79.99 is due on August 30th.", "Pay Spectrum Bill ($79.99)")]
    [InlineData("Your Spectrum bill of $65.98 is due.", "The Spectrum bill for $65.98 is due.")]
    [InlineData("Spectrum Bill Due", "Reminder to pay the Spectrum bill.")]
    // The habit nudge, recreated daily in two channels with different phrasing.
    [InlineData("Check in on your habits! You completed 0 out of 4 today.",
                "You completed 0 out of 4 habits today; take a moment to check in on your progress.")]
    [InlineData("Habit Check-in", "Habit Check-in Required")]
    public void CatchesTheSameThingWordedDifferently(string a, string b)
        => Assert.True(DuplicateGuard.IsDuplicate(a, Base, b, Base),
                       $"similarity was {DuplicateGuard.Similarity(a, b):F2}");

    [Theory]
    // Share a verb, share the word "bill", and are entirely different obligations.
    [InlineData("Pay credit card bills", "Pay Spectrum Bill ($79.99)")]
    [InlineData("Pay credit card bills", "Transfer money to Robinhood")]
    [InlineData("Shop for car insurance", "Check Alcove claims")]
    [InlineData("Start your resume", "Complete paper")]
    [InlineData("Schedule blood test", "Schedule service ticket for the property")]
    [InlineData("Go to the bank", "Go to the gym")]
    public void KeepsGenuinelyDifferentObligations(string a, string b)
        => Assert.False(DuplicateGuard.IsDuplicate(a, Base, b, Base),
                        $"similarity was {DuplicateGuard.Similarity(a, b):F2}");

    [Fact]
    public void NextMonthsBillIsNotThisMonths()
        => Assert.False(DuplicateGuard.IsDuplicate(
            "Pay Spectrum Bill", Base.AddDays(30), "Pay Spectrum Bill", Base));

    [Fact]
    public void SameBillNoticedAgainHoursLaterIsADuplicate()
        => Assert.True(DuplicateGuard.IsDuplicate(
            "Pay Spectrum Bill", Base.AddHours(4), "Spectrum bill is due", Base));

    [Fact]
    public void EmptyTextIsNeverADuplicate()
    {
        Assert.False(DuplicateGuard.IsDuplicate("", Base, "anything", Base));
        Assert.False(DuplicateGuard.IsDuplicate("anything", Base, "   ", Base));
    }

    [Fact]
    public void FingerprintDropsNumbersAndFiller()
    {
        var f = DuplicateGuard.Fingerprint("Please remind me to pay the $79.99 Spectrum bill today");
        Assert.Contains("spectrum", f);
        Assert.Contains("pay", f);
        Assert.DoesNotContain("today", f);
        Assert.DoesNotContain("please", f);
        Assert.DoesNotContain("79.99", f);
    }
}
