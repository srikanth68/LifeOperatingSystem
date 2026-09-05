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

    [Theory]
    // The tree-trimming conversation. San said this, called nothing, and moved straight
    // on to "you have 13 pending actions" -- so the task stayed open while reading as
    // done. Past tense alone never saw it.
    [InlineData("I see you've taken care of the tree trimming. I will now mark the task \"Arrange Tree trimming at 15128 Scoter Street\" as complete.")]
    [InlineData("I will now mark that as complete.")]
    [InlineData("I'll create that reminder for you.")]
    [InlineData("Let me mark that complete.")]
    [InlineData("I'm going to add these to your list.")]
    [InlineData("I will go ahead and delete it.")]
    public void FlagsAnAnnouncedActionThatNeverHappened(string reply)
    {
        Assert.True(WriteClaimCheck.ClaimsUnverifiedWrite(reply, NoTools));
        Assert.True(WriteClaimCheck.ClaimsUnverifiedWrite(reply, ReadsOnly));
    }

    [Theory]
    // Intent that is genuinely waiting on the user. San is right to say these and right
    // not to act, and nudging it here would punish it for asking a sensible question.
    [InlineData("I will add these once you tell me the timing.")]
    [InlineData("I'll set it up when you give me a time.")]
    [InlineData("I'll create it after you confirm the address.")]
    [InlineData("I will mark it complete if that is the right one.")]
    // Not writes at all.
    [InlineData("Let me check your reminders first.")]
    [InlineData("I'll look that up for you.")]
    [InlineData("Let me pull up what is at the top of that list.")]
    public void IgnoresConditionalIntentAndNonWrites(string reply)
        => Assert.False(WriteClaimCheck.ClaimsUnverifiedWrite(reply, NoTools));

    [Fact]
    public void AConditionalInOneSentenceDoesNotExcuseADeclarationInTheNext()
    {
        // The excuse has to be attached to the claim, not merely present somewhere in
        // the reply -- otherwise one stray "if" launders the whole message.
        Assert.True(WriteClaimCheck.ClaimsUnverifiedWrite(
            "I can look that up if you want. I will now mark it complete.", NoTools));
    }

    [Fact]
    public void AnAnnouncedActionIsFineWhenItWasActuallyCarriedOut()
        => Assert.False(WriteClaimCheck.ClaimsUnverifiedWrite(
            "I will now mark the task as complete.", ["action_complete"]));

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
