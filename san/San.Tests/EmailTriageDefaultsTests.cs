using San.Application;

namespace San.Tests;

// Email triage was filling NorthStar with action items the user never opened. They
// wanted reminders, which is the surface they actually look at.
//
// Two halves, and the second is the one that holds: the prompt asks for reminders, and
// the tool that would have created an action item is withheld from the run entirely.
// The prompt is user-editable and stored in settings, so an instruction in it is a
// request; a tool that is not on the table is a guarantee.
public class EmailTriageDefaultsTests
{
    [Fact]
    public void TriageCannotCreateNorthStarActionItems()
        => Assert.Contains("action_add", EmailTriageDefaults.WithheldTools);

    [Fact]
    public void TheDefaultPromptNoLongerAsksForThem()
    {
        // The default prompt is what a fresh install runs, and it used to say
        // "add an action item to NorthStar for something that needs doing".
        Assert.DoesNotContain("action item to NorthStar", EmailTriageDefaults.Prompt);
        Assert.Contains("Do NOT add NorthStar action items", EmailTriageDefaults.Prompt);
    }

    [Fact]
    public void ItTellsTheModelWhatDateToUseInstead()
    {
        // reminder_create requires a due date, so "no stated deadline" would otherwise
        // be a reason to create nothing at all — which is how the action item got
        // reached for in the first place.
        Assert.Contains("tomorrow morning", EmailTriageDefaults.Prompt);
    }

    [Fact]
    public void NothingElseIsWithheld()
    {
        // Withholding is a blunt instrument. Reminders, alerts, calendar events,
        // property tasks and memory saves are all still on the table, and the list
        // should stay short enough to justify each entry.
        Assert.Single(EmailTriageDefaults.WithheldTools);
    }
}
