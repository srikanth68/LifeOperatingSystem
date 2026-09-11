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

    // ── reminder vs calendar event ──────────────────────────────────────────────

    [Fact]
    public void ThePromptGivesTheModelATestItCanApply()
    {
        // No code decides this, deliberately. Nothing in an email reliably says
        // whether "Tuesday 3pm" is an appointment or a deadline -- that is judgement,
        // and it is the kind a language model is actually good at. What it needed was
        // a test it can apply rather than two tool names and no way to choose.
        Assert.Contains("BUSY then", EmailTriageDefaults.Prompt);
        Assert.Contains("CALENDAR EVENT", EmailTriageDefaults.Prompt);
    }

    [Fact]
    public void AndAWorkedExampleOfEachSide()
    {
        // The abstract rule alone left the two nearest cases ambiguous: both are a
        // weekday plus a time, and only one of them occupies the day.
        Assert.Contains("Dentist Tuesday 3pm", EmailTriageDefaults.Prompt);
        Assert.Contains("card payment due Tuesday", EmailTriageDefaults.Prompt);
    }

    [Fact]
    public void AlertsAreNoLongerOfferedForDatedThings()
    {
        // Alerts used to sit in the same breath as reminders and events, which made a
        // third plausible answer to a two-way question. They are for thresholds.
        Assert.Contains("threshold being crossed, not for anything with a fixed date",
            EmailTriageDefaults.Prompt);
    }
}
