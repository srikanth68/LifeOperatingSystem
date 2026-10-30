using San.Application;

namespace San.Tests;

// What the medical rule promises. It rides every turn's system prompt (see
// ChatController), so there is no longer a question of when it is attached.
public class MedicalBoundaryTests
{
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
