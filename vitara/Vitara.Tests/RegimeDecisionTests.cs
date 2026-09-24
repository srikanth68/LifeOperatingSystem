using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// Whether a step change becomes the new normal. Detection is statistics; this is the
// judgement call on top of it, and getting it wrong in the quiet direction means the
// system adopts a decline as normal and then reports that nothing is wrong.
public class RegimeDecisionTests
{
    private static readonly DateOnly ChangePoint = new(2026, 9, 1);

    [Fact]
    public void AnUnexplainedAdverseStepIsNotAdopted()
    {
        Assert.False(RegimeDecision.ShouldAdopt(MetricKeys.RestingHeartRate, "up", null));
        Assert.False(RegimeDecision.ShouldAdopt(MetricKeys.HrvRmssd, "down", null));
        Assert.False(RegimeDecision.ShouldAdopt(MetricKeys.TotalSleepMinutes, "down", null));
    }

    [Fact]
    public void AnExplainedStepIsAdoptedEitherWay()
    {
        Assert.True(RegimeDecision.ShouldAdopt(MetricKeys.RestingHeartRate, "up", "medication beta blocker"));
        Assert.True(RegimeDecision.ShouldAdopt(MetricKeys.HrvRmssd, "down", "a new ring (Gen4)"));
    }

    [Fact]
    public void AStepTheHarmlessWayIsAdopted()
    {
        Assert.True(RegimeDecision.ShouldAdopt(MetricKeys.RestingHeartRate, "down", null));
        Assert.True(RegimeDecision.ShouldAdopt(MetricKeys.HrvRmssd, "up", null));
    }

    [Fact]
    public void AMetricWithNoKnownDirectionIsNeverTreatedAsAdverse()
    {
        Assert.False(MetricDirection.IsAdverse("some_new_analyte", "up"));
        Assert.False(MetricDirection.IsAdverse(MetricKeys.Steps, "down"));
        Assert.True(RegimeDecision.ShouldAdopt("some_new_analyte", "up", null));
    }

    [Fact]
    public void AnInterventionNearTheChangePointExplainsIt()
    {
        var started = new List<Intervention>
        {
            new() { Kind = "medication", Name = "beta blocker", StartedOnLocal = ChangePoint.AddDays(-3) },
        };

        Assert.Equal("medication beta blocker", RegimeDecision.Explain(ChangePoint, interventions: started));
    }

    [Fact]
    public void AnInterventionLongBeforeTheChangeExplainsNothing()
    {
        var started = new List<Intervention>
        {
            new() { Kind = "supplement", Name = "magnesium", StartedOnLocal = ChangePoint.AddDays(-60), EndedOnLocal = ChangePoint.AddDays(-40) },
        };

        Assert.Null(RegimeDecision.Explain(ChangePoint, interventions: started));
    }

    [Fact]
    public void ADeviceSwapExplainsAStepButOnlyWhenItStarts()
    {
        var swapped = new List<Device> { new() { Kind = "ring", Model = "Gen4", ActiveFromLocal = ChangePoint.AddDays(2) } };
        var oldRing = new List<Device> { new() { Kind = "ring", Model = "Gen3", ActiveFromLocal = ChangePoint.AddDays(-200) } };

        Assert.Contains("a new ring (Gen4)", RegimeDecision.Explain(ChangePoint, devices: swapped));
        Assert.Null(RegimeDecision.Explain(ChangePoint, devices: oldRing));
    }

    [Fact]
    public void IllnessAndTravelAroundTheChangeAreOfferedAsExplanations()
    {
        var ill = new List<ExcludedPeriod>
        {
            new() { StartLocal = ChangePoint.AddDays(-2), EndLocal = ChangePoint.AddDays(5), Reason = "training_block" },
        };
        var away = new List<TravelPeriod>
        {
            new() { StartLocal = ChangePoint.AddDays(-1), EndLocal = ChangePoint.AddDays(6) },
        };

        Assert.Equal("training block", RegimeDecision.Explain(ChangePoint, excluded: ill));
        Assert.Equal("travel", RegimeDecision.Explain(ChangePoint, travel: away));
    }

    // The finding is the only place an unadopted shift is visible, so its wording and
    // rank carry the whole point.
    [Fact]
    public void AnUnadoptedShiftIsTheLoudestVersionOfTheFinding()
    {
        var change = new RegimeChange(ChangePoint, 52, 57, 2.1, 20);

        var unadopted = FindingDetectors.FromRegimeChange(MetricKeys.RestingHeartRate, change, null, ChangePoint.AddDays(20), adopted: false);
        var adopted = FindingDetectors.FromRegimeChange(MetricKeys.RestingHeartRate, change, null, ChangePoint.AddDays(20));
        var explained = FindingDetectors.FromRegimeChange(MetricKeys.RestingHeartRate, change, "medication beta blocker", ChangePoint.AddDays(20));

        Assert.Equal("high", unadopted.Severity);
        Assert.Equal("notable", adopted.Severity);
        Assert.Equal("info", explained.Severity);
        Assert.Contains("left where it was", unadopted.Summary);
        // The two levels are in the sentence, not only the evidence blob: "52 → 57" is
        // the part a person can act on.
        Assert.Contains("52", unadopted.Summary);
        Assert.Contains("57", unadopted.Summary);
    }
}
