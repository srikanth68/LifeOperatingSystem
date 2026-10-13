using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// A change aimed at a lab, judged draw to draw (Vitara ADR 0022).
public class LabTargetEvalTests
{
    private static readonly DateOnly Start = new(2026, 3, 1);

    private static Observation Draw(string metric, double value, DateOnly day) => new()
    {
        Metric = metric, Value = value, ObservedDateLocal = day, ObservedAtLocal = day.ToDateTime(new TimeOnly(8, 0)),
        LabPanelId = Guid.NewGuid(), Tier = Tiers.Sparse,
    };

    // A two-ended range for LDL, so the half-width rule is the one under test.
    private static readonly List<ReferenceRange> LdlRange =
        [new() { Metric = MetricKeys.Ldl, Low = 40, High = 130, Unit = "mg/dL" }];   // half width 45

    private static Evaluation Eval(List<Observation> obs, DateOnly asOf, List<Intervention>? others = null)
    {
        var fibre = new Intervention { Name = "More fibre", Kind = "protocol", StartedOnLocal = Start, TargetMetric = MetricKeys.Ldl };
        return InterventionEval.Run((others ?? []).Append(fibre).ToList(), obs, asOf, ranges: LdlRange)
            .Single(e => e.Name == "More fibre");
    }

    [Fact]
    public void ADrawAfterTheIntervalThatMovedMoreThanHalfTheRangeIsImproved_LowConfidence()
    {
        var obs = new List<Observation> { Draw(MetricKeys.Ldl, 160, Start.AddDays(-20)), Draw(MetricKeys.Ldl, 105, Start.AddDays(60)) };

        var e = Eval(obs, Start.AddDays(70));

        Assert.Equal(InterventionEval.Verdicts.Improved, e.Verdict);
        Assert.Equal("low", e.Confidence);
        Assert.Contains(e.Caveats, c => c.Contains("two blood draws"));
    }

    [Fact]
    public void ASmallerMoveIsNoMeaningfulChange()
    {
        var obs = new List<Observation> { Draw(MetricKeys.Ldl, 140, Start.AddDays(-20)), Draw(MetricKeys.Ldl, 120, Start.AddDays(60)) };
        Assert.Equal(InterventionEval.Verdicts.NoChange, Eval(obs, Start.AddDays(70)).Verdict);
    }

    [Fact]
    public void ASecondDrawTwentyDaysInIsTooEarly_WithTheDateAVerdictBecomesPossible()
    {
        var obs = new List<Observation> { Draw(MetricKeys.Ldl, 160, Start.AddDays(-20)), Draw(MetricKeys.Ldl, 110, Start.AddDays(20)) };

        var e = Eval(obs, Start.AddDays(25));

        Assert.Equal(InterventionEval.Verdicts.TooEarly, e.Verdict);
        Assert.Equal(Start.AddDays(42), e.EarliestVerdict);   // lipids: six weeks
    }

    [Fact]
    public void AnotherChangeBetweenTheDrawsCannotBeSeparated()
    {
        var obs = new List<Observation> { Draw(MetricKeys.Ldl, 160, Start.AddDays(-20)), Draw(MetricKeys.Ldl, 105, Start.AddDays(60)) };
        var other = new Intervention { Name = "Statin", Kind = "medication", StartedOnLocal = Start.AddDays(10) };

        Assert.Equal(InterventionEval.Verdicts.CannotSeparate, Eval(obs, Start.AddDays(70), [other]).Verdict);
    }

    [Fact]
    public void NoDrawBeforeTheStartNamesTheTestToOrder()
    {
        var obs = new List<Observation> { Draw(MetricKeys.Ldl, 105, Start.AddDays(60)) };

        var e = Eval(obs, Start.AddDays(70));

        Assert.Equal(InterventionEval.Verdicts.NotEnoughData, e.Verdict);
        Assert.Contains("LDL", e.Statement);
    }
}
