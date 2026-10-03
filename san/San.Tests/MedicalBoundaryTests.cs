using San.Application;

namespace San.Tests;

// When the medical rule is attached, and what it promises.
//
// The rule costs ~230 tokens, so it is only attached when the turn is about a body —
// and the matching has to be generous in one specific direction. "Is that bad?" is a
// health question when the previous reply was about a resting heart rate and a
// scheduling question when it was about a meeting, and only the previous turn can
// tell those apart.
public class MedicalBoundaryTests
{
    [Theory]
    [InlineData("should I be worried about my resting heart rate")]
    [InlineData("what does my LDL mean")]
    [InlineData("my HbA1c came back at 5.9")]
    [InlineData("I've had a headache and some chest pain")]
    [InlineData("can I double my vitamin D dose")]
    [InlineData("what should I ask the doctor on Tuesday")]
    [InlineData("is my thyroid ok")]
    public void A_question_about_a_body_attaches_the_rule(string message) =>
        Assert.True(MedicalBoundary.Applies(message, null));

    [Theory]
    [InlineData("what did I spend at the hardware shop")]
    [InlineData("remind me to call the plumber at four")]
    [InlineData("how is NVDA doing")]
    [InlineData("what's on today")]
    public void An_ordinary_question_does_not_pay_for_it(string message) =>
        Assert.False(MedicalBoundary.Applies(message, null));

    [Fact]
    public void A_follow_up_inherits_from_what_was_just_discussed()
    {
        // "Is that bad?" is the question people actually ask, and on its own it carries
        // no cue at all.
        Assert.True(MedicalBoundary.Applies("is that bad?", "Your resting heart rate is 59 bpm, 4 above your usual."));
        Assert.False(MedicalBoundary.Applies("is that bad?", "That invoice is 12 days overdue."));
    }

    [Fact]
    public void The_rule_forbids_the_four_things_that_matter()
    {
        var text = MedicalBoundary.Text;

        Assert.Contains("not a doctor", text);
        Assert.Contains("Never name a condition", text);
        Assert.Contains("never recommend or adjust a medication", text);
        // The quiet one: an empty findings list is not an all-clear.
        Assert.Contains("Never let an absence of findings stand as reassurance", text);
    }

    [Fact]
    public void It_tells_the_model_to_answer_from_tools_rather_than_from_knowledge()
    {
        // The failure being designed against is not a fabricated diagnosis — it is a
        // fluent, plausible paragraph the user cannot separate from their own data.
        Assert.Contains("Answer from their measurements, not from medical knowledge", MedicalBoundary.Text);
        Assert.Contains("If a tool has not been called, you do not know", MedicalBoundary.Text);
    }

    [Fact]
    public void Emergencies_are_handed_straight_on()
    {
        Assert.Contains("emergency care now", MedicalBoundary.Text);
        Assert.Contains("not yours to triage", MedicalBoundary.Text);
    }
}
