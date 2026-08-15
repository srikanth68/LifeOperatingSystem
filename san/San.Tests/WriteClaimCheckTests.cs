using San.Application;

namespace San.Tests;

// These cases come from a real conversation. Asked for ten reminders, San answered
// "I have saved 10 reminders" and created none — the reminders list showed nothing
// added that day, while the six action_add calls from an earlier turn were all there.
// The detector exists to catch exactly that, so the sentences it actually produced are
// the tests, alongside the offers and capability statements it must NOT flag.
public class WriteClaimCheckTests
{
    private static readonly string[] NoTools = [];
    private static readonly string[] ReadsOnly = ["reminders_list", "agenda_now", "actions_pending"];

    [Theory]
    // Verbatim from the conversation that exposed this.
    [InlineData("I have saved 10 reminders.")]
    [InlineData("All reminders are set for Monday, August 17, 2026, at 8:00 PM. I have saved 8 reminders.")]
    [InlineData("I've created the reminder for you.")]
    [InlineData("I have now successfully added all six items to your queue.")]
    [InlineData("The reminder has been created.")]
    [InlineData("Your tasks are now scheduled.")]
    [InlineData("I just logged your workout.")]
    public void FlagsACompletionClaimWhenNothingWasWritten(string reply)
    {
        Assert.True(WriteClaimCheck.ClaimsUnverifiedWrite(reply, NoTools));
        Assert.True(WriteClaimCheck.ClaimsUnverifiedWrite(reply, ReadsOnly));
    }

    [Theory]
    // Offers, capabilities and questions are the whole reason the pattern is past-tense
    // only: San suggesting an action must never be mistaken for San claiming one.
    [InlineData("I can set that up for you — when should it fire?")]
    [InlineData("Shall I schedule it for Monday night?")]
    [InlineData("Would you like me to create reminders for those?")]
    [InlineData("I will add these once you tell me the timing.")]
    [InlineData("You have 4 reminders due this week.")]
    [InlineData("Which items should I set reminders for, and when should each one go off?")]
    [InlineData("Your sleep score is 82 and your readiness is 74.")]
    public void IgnoresOffersCapabilitiesAndReads(string reply)
        => Assert.False(WriteClaimCheck.ClaimsUnverifiedWrite(reply, NoTools));

    [Fact]
    public void StaysQuietWhenAWriteToolActuallyRan()
        => Assert.False(WriteClaimCheck.ClaimsUnverifiedWrite(
            "I have saved 10 reminders.", ["reminder_create"]));

    [Theory]
    [InlineData("reminder_create", true)]
    [InlineData("action_add", true)]
    [InlineData("workout_log", true)]
    [InlineData("action_complete", true)]
    [InlineData("goal_progress_set", true)]
    [InlineData("northstar_sync", true)]
    [InlineData("reminders_list", false)]
    [InlineData("actions_pending", false)]
    [InlineData("agenda_now", false)]
    [InlineData("vitara_health", false)]
    [InlineData("maaya_search", false)]
    public void ClassifiesToolsByTheirVerb(string tool, bool isWrite)
        => Assert.Equal(isWrite, WriteClaimCheck.IsWriteTool(tool));

    [Fact]
    public void TreatsAnEmptyReplyAsNoClaim()
        => Assert.False(WriteClaimCheck.ClaimsUnverifiedWrite("   ", NoTools));
}
