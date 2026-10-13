using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// Arithmetic, learned or experiment, always labelled (Vitara ADR 0024).
public class ScenarioTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 1);

    private static List<(DateOnly, double)> Nights(double minutes, int days = 60) =>
        Enumerable.Range(0, days).Select(i => (AsOf.AddDays(-i), minutes)).ToList();

    private static Scenarios.Inputs Inputs(List<(DateOnly, double)>? nights = null, List<(DateOnly, double)>? calories = null) =>
        new(nights ?? Nights(450), calories ?? [], [], [], [], AsOf);

    [Fact]
    public void FiveShortNightsAreArithmetic_AndExact()
    {
        // Sleeping 7 h 30 every night; the need clamps to that. Five nights of 6 h owe
        // 90 minutes each.
        var owed = Scenarios.SleepOwed(Nights(450), AsOf, 360, 5)!.Value;
        Assert.Equal(0, owed.Before);
        Assert.Equal(450, owed.After);

        var result = Scenarios.Answer("short_sleep", 360, 5, Inputs());
        Assert.Equal(ScenarioModes.Arithmetic, result.Mode);
        Assert.Contains(result.Answers, a => a.Mode == ScenarioModes.Arithmetic && a.To == 450);
    }

    [Fact]
    public void ExtraTrainingRaisesTheLoadRamp()
    {
        var calories = Enumerable.Range(0, 28).Select(i => (AsOf.AddDays(-i), 500.0)).ToList();
        var ramp = Scenarios.LoadRamp(calories, AsOf, 400, 5)!.Value;

        Assert.Equal(1.0, ramp.Before);
        Assert.True(ramp.After > 1.2);
    }

    [Fact]
    public void AMedicationIsNeverPredicted_AndSaysToTalkToTheDoctor()
    {
        var result = Scenarios.Answer("new_medication", null, 5, Inputs());

        Assert.Equal(ScenarioModes.Experiment, result.Mode);
        Assert.All(result.Answers, a => { Assert.Null(a.To); Assert.False(a.Supported); });
        Assert.Contains("doctor", result.Experiment!.DoctorNote);
    }

    [Fact]
    public void ADietNeverTriedIsAnExperiment_WithAVerdictDate()
    {
        var result = Scenarios.Answer("new_diet", null, 5, Inputs());

        Assert.Equal(ScenarioModes.Experiment, result.Mode);
        Assert.Equal(InterventionEval.RunInDays + InterventionEval.WindowDays, result.Experiment!.DaysUntilFirstVerdict);
        Assert.NotEmpty(result.Experiment.SuggestedTargets);
    }

    [Fact]
    public void WithNoHistoryNothingIsCalculated_AndItSaysSo()
    {
        var result = Scenarios.Answer("short_sleep", 360, 5, Inputs(nights: []));
        Assert.Single(result.Answers);
        Assert.False(result.Answers[0].Supported);
    }

    [Fact]
    public void EveryChangeIsAnswered()
    {
        foreach (var change in Scenarios.Changes)
        {
            var result = Scenarios.Answer(change, null, 5, Inputs());
            Assert.NotEmpty(result.Answers);
            Assert.Contains(result.Mode, new[] { ScenarioModes.Arithmetic, ScenarioModes.Learned, ScenarioModes.Experiment });
        }
    }
}
