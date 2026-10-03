using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// Reading several measures together, and mostly declining to.
//
// A composite detector is the easiest thing in this system to make fire constantly:
// count metrics moving the wrong way, fire at two, and on thirty correlated metrics
// something is always moving. Nearly every test here is about one of the four things
// that stop that -- correlated measures not voting twice, unmeasured not counting as
// steady, the window following the cadence, and a proportion floor on top of a count.
public class PatternTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    // A clean ramp. Monotonic on purpose: Mann-Kendall is a test of monotonicity, and
    // a scenario that wants "this is definitely trending" should say so unambiguously
    // rather than hoping noise lands the right way.
    private static List<Observation> Ramp(
        string metric, double from, double to, int days = 90, int everyDays = 1, string tier = Tiers.Dense)
    {
        var points = days / everyDays;
        return Enumerable.Range(0, points)
            .Select(i => new Observation
            {
                Metric = metric,
                Value = from + (to - from) * i / (double)(points - 1),
                ObservedDateLocal = Today.AddDays(-days + i * everyDays),
                ObservedAtLocal = Today.AddDays(-days + i * everyDays).ToDateTime(new TimeOnly(8, 0)),
                Tier = tier,
            })
            .ToList();
    }

    private static List<Observation> Flat(string metric, double value, int days = 90, int everyDays = 1,
        string tier = Tiers.Dense)
    {
        // Not perfectly flat: a constant series has zero variance and every tie, which
        // is a different edge case from "measured often, going nowhere".
        var points = days / everyDays;
        return Enumerable.Range(0, points)
            .Select(i => new Observation
            {
                Metric = metric,
                Value = value + (i % 3 - 1) * 0.3,
                ObservedDateLocal = Today.AddDays(-days + i * everyDays),
                ObservedAtLocal = Today.AddDays(-days + i * everyDays).ToDateTime(new TimeOnly(8, 0)),
                Tier = tier,
            })
            .ToList();
    }

    // Two draws of one analyte, each belonging to its own panel.
    private static List<Observation> Draws(string metric, double before, double after, int monthsApart = 6)
    {
        var first = Today.AddMonths(-monthsApart);
        return
        [
            Panel(metric, before, first),
            Panel(metric, after, Today.AddDays(-7)),
        ];
    }

    private static Observation Panel(string metric, double value, DateOnly day) => new()
    {
        Metric = metric,
        Value = value,
        ObservedDateLocal = day,
        ObservedAtLocal = day.ToDateTime(new TimeOnly(8, 0)),
        Tier = Tiers.Sparse,
        LabPanelId = Guid.NewGuid(),
    };

    private static PatternResult Find(IEnumerable<Observation> observations, string key) =>
        Patterns.Run(observations.ToList(), Today, ReferenceRanges.Seed, "male", 44)
            .First(p => p.Key == key);

    // ── It fires when it should ─────────────────────────────────────────────────

    [Fact]
    public void FourMetabolicMeasuresMovingTheSameWayIsAPattern()
    {
        // The example from the spec: weight up, waist up, triglycerides up, fasting
        // insulin up. Each one alone is a shrug. Together they are a direction, and no
        // single-metric detector can see it.
        var observations = Ramp(MetricKeys.WeightKg, 78, 84, days: 365, everyDays: 10, tier: Tiers.Medium)
            .Concat(Ramp(MetricKeys.WaistCircumferenceCm, 88, 95, days: 365, everyDays: 20, tier: Tiers.Medium))
            .Concat(Draws(MetricKeys.Triglycerides, 110, 180))
            .Concat(Draws(MetricKeys.FastingInsulin, 7, 16));

        var pattern = Find(observations, "metabolic_drift");

        Assert.True(pattern.Fires);
        Assert.Null(pattern.HeldBecause);
        Assert.Equal(4, pattern.Moving);
    }

    [Fact]
    public void RecoveryMeasuresDriftingForMonthsAreNotTheIllnessSignal()
    {
        // Deliberately a different detector from early illness: that one reads two
        // days, this one reads ninety, and a cold cannot produce a ninety-day
        // monotonic trend. Both can be true at once and they mean different things.
        var observations = Ramp(MetricKeys.RestingHeartRate, 52, 60)
            .Concat(Ramp(MetricKeys.HrvRmssd, 68, 44));

        var pattern = Find(observations, "autonomic_strain");

        Assert.True(pattern.Fires);
        Assert.Equal("3-month trend", pattern.Components.First(c => c.Metric == MetricKeys.RestingHeartRate).Basis);
    }

    // ── The good one ────────────────────────────────────────────────────────────

    [Fact]
    public void WeightComingOffWithTheWaistAndLeanMassHoldingIsSaidOutLoud()
    {
        // The most useful sentence this system can produce, and the one most health
        // software never says. A detector that only speaks when something is wrong
        // teaches people to dread opening it.
        var observations = Ramp(MetricKeys.WeightKg, 92, 85, days: 365, everyDays: 10, tier: Tiers.Medium)
            .Concat(Ramp(MetricKeys.WaistCircumferenceCm, 104, 96, days: 365, everyDays: 20, tier: Tiers.Medium))
            .Concat(Flat(MetricKeys.LeanMassKg, 62, days: 365, everyDays: 10, tier: Tiers.Medium));

        var pattern = Find(observations, "favourable_body_change");

        Assert.True(pattern.Fires);
        Assert.Contains("Waist", pattern.Statement);
    }

    [Fact]
    public void TheGoodNewsIsWithheldWhenLeanMassIsNotBeingMeasured()
    {
        // THE REFUSAL THAT MATTERS MOST HERE. Weight down and waist down looks like an
        // unqualified success, and without lean mass nobody can say it was -- weight
        // alone cannot tell a good loss from a bad one, which is the spec's own point.
        var observations = Ramp(MetricKeys.WeightKg, 92, 85, days: 365, everyDays: 10, tier: Tiers.Medium)
            .Concat(Ramp(MetricKeys.WaistCircumferenceCm, 104, 96, days: 365, everyDays: 20, tier: Tiers.Medium));

        var pattern = Find(observations, "favourable_body_change");

        Assert.False(pattern.Fires);
        Assert.Contains("lean mass", pattern.HeldBecause);
    }

    [Fact]
    public void WeightComingOffWithLeanMassIsTheOtherOutcome()
    {
        var observations = Ramp(MetricKeys.WeightKg, 92, 85, days: 365, everyDays: 10, tier: Tiers.Medium)
            .Concat(Ramp(MetricKeys.LeanMassKg, 64, 59, days: 365, everyDays: 10, tier: Tiers.Medium));

        var loss = Find(observations, "lean_mass_loss");
        var good = Find(observations, "favourable_body_change");

        Assert.True(loss.Fires);
        Assert.False(good.Fires);          // the two cannot both be true
    }

    [Fact]
    public void LeanMassFallingWithoutWeightFallingIsNotThisPattern()
    {
        // The gate. Lean mass falling while weight holds is a different situation and
        // possibly a worse one, but it is not "the weight you are losing is the wrong
        // kind" and should not be described as though it were.
        var observations = Flat(MetricKeys.WeightKg, 85, days: 365, everyDays: 10, tier: Tiers.Medium)
            .Concat(Ramp(MetricKeys.LeanMassKg, 64, 59, days: 365, everyDays: 10, tier: Tiers.Medium));

        var pattern = Find(observations, "lean_mass_loss");

        Assert.False(pattern.Fires);
        Assert.Contains("weight is not coming down", pattern.HeldBecause);
    }

    // ── What stops it firing ────────────────────────────────────────────────────

    [Fact]
    public void OneMeasureMovingIsNotAPattern()
    {
        // Drift already says this, on its own, better. A pattern that fires on one
        // component is a second notification about the same number.
        var observations = Ramp(MetricKeys.WeightKg, 78, 84, days: 365, everyDays: 10, tier: Tiers.Medium)
            .Concat(Flat(MetricKeys.WaistCircumferenceCm, 90, days: 365, everyDays: 20, tier: Tiers.Medium))
            .Concat(Draws(MetricKeys.Triglycerides, 110, 112))
            .Concat(Draws(MetricKeys.FastingInsulin, 7, 7.2));

        var pattern = Find(observations, "metabolic_drift");

        Assert.False(pattern.Fires);
        Assert.Equal(1, pattern.Moving);
    }

    [Fact]
    public void TwoOfManyIsACoincidenceRatherThanADirection()
    {
        // The proportion floor on top of the count. Two of eight measures moving is
        // what eight measures do; two of three is a direction. Without this, adding an
        // analyte to the catalogue would make the pattern easier to fire, which is
        // backwards.
        var observations = Ramp(MetricKeys.WeightKg, 78, 82, days: 365, everyDays: 10, tier: Tiers.Medium)
            .Concat(Ramp(MetricKeys.WaistCircumferenceCm, 88, 94, days: 365, everyDays: 20, tier: Tiers.Medium))
            .Concat(Draws(MetricKeys.Triglycerides, 110, 112))
            .Concat(Draws(MetricKeys.FastingInsulin, 7, 7.1))
            .Concat(Draws(MetricKeys.Glucose, 92, 93))
            .Concat(Draws(MetricKeys.Hba1c, 5.3, 5.3))
            .Concat(Draws(MetricKeys.Alt, 22, 23));

        var pattern = Find(observations, "metabolic_drift");

        Assert.Equal(2, pattern.Moving);
        Assert.False(pattern.Fires);
        Assert.Contains("fewer than half", pattern.HeldBecause);
    }

    [Fact]
    public void ADerivedValueDoesNotVoteAlongsideWhatItIsDerivedFrom()
    {
        // Non-HDL is total cholesterol minus HDL. Counting it as independent evidence
        // turns one blood draw into two votes, and two votes is this pattern's floor.
        var observations = Draws(MetricKeys.TotalCholesterol, 180, 250)
            .Concat(Draws(MetricKeys.Hdl, 55, 54))
            .Concat(Draws(MetricKeys.NonHdl, 125, 196));

        var pattern = Find(observations, "lipid_drift");

        var nonHdl = pattern.Components.First(c => c.Metric == MetricKeys.NonHdl);

        Assert.False(nonHdl.Counts);
        Assert.Equal(Movement.Unfavourable, nonHdl.Movement);   // still reported
        Assert.Equal(1, pattern.Moving);
        Assert.False(pattern.Fires);
    }

    [Fact]
    public void ADerivedValueStandsAloneWhenItsInputsAreNotInThePattern()
    {
        // HOMA-IR sits in the metabolic pattern alongside glucose and insulin, so it
        // is suppressed there. Here neither input was drawn, so it is the only thing
        // carrying that information and must count.
        var observations = Ramp(MetricKeys.WeightKg, 78, 84, days: 365, everyDays: 10, tier: Tiers.Medium)
            .Concat(Draws(MetricKeys.HomaIr, 1.4, 3.2));

        var pattern = Find(observations, "metabolic_drift");

        Assert.True(pattern.Components.First(c => c.Metric == MetricKeys.HomaIr).Counts);
        Assert.Equal(2, pattern.Moving);
        Assert.True(pattern.Fires);
    }

    // ── Missing is not steady ───────────────────────────────────────────────────

    [Fact]
    public void AMeasureNobodyTookIsNotAMeasureThatIsNotMoving()
    {
        var pattern = Find(Ramp(MetricKeys.WeightKg, 78, 84, days: 365, everyDays: 10, tier: Tiers.Medium),
            "metabolic_drift");

        var insulin = pattern.Components.First(c => c.Metric == MetricKeys.FastingInsulin);

        Assert.Equal(Movement.Unmeasured, insulin.Movement);
        Assert.NotEqual(Movement.Steady, insulin.Movement);
        Assert.Equal(7, pattern.Unmeasured);
        Assert.Equal(1, pattern.Counted);
    }

    [Fact]
    public void TheGapsAreNamedSoSomebodyKnowsWhatToAskFor()
    {
        var pattern = Find(Ramp(MetricKeys.WeightKg, 78, 84, days: 365, everyDays: 10, tier: Tiers.Medium),
            "metabolic_drift");

        Assert.Contains("not being measured often enough", pattern.Statement);
    }

    [Fact]
    public void TooFewReadingsToJudgeIsSaidRatherThanCalledSteady()
    {
        // Four tape measurements in a year is not a waist trend, and reporting it as
        // "steady" would be an assertion built on four points.
        var sparse = Ramp(MetricKeys.WaistCircumferenceCm, 88, 96, days: 360, everyDays: 90, tier: Tiers.Medium);

        var pattern = Find(sparse, "metabolic_drift");
        var waist = pattern.Components.First(c => c.Metric == MetricKeys.WaistCircumferenceCm);

        Assert.Equal(Movement.Unmeasured, waist.Movement);
        Assert.Contains("too few", waist.Detail);
    }

    [Fact]
    public void OneDrawCannotShowADirection()
    {
        var one = new List<Observation> { Panel(MetricKeys.Triglycerides, 240, Today.AddDays(-20)) };

        var pattern = Find(one, "metabolic_drift");
        var trigs = pattern.Components.First(c => c.Metric == MetricKeys.Triglycerides);

        Assert.Equal(Movement.Unmeasured, trigs.Movement);
        Assert.Contains("two draws", trigs.Detail);
    }

    [Fact]
    public void NothingMeasuredAtAllSaysSoRatherThanReportingNoPattern()
    {
        var pattern = Find(new List<Observation>(), "metabolic_drift");

        Assert.False(pattern.Fires);
        Assert.Contains("Nothing here can be judged yet", pattern.Statement);
        Assert.Contains("measured often enough to say", pattern.HeldBecause);
    }

    // ── How a move is judged ────────────────────────────────────────────────────

    [Fact]
    public void ALabMoveIsMeasuredAgainstTheWidthOfItsRange()
    {
        // The same rule the lab detector uses. Twenty per cent of a TSH is noise and
        // twenty per cent of an LDL is a different person's risk, and two parts of the
        // system disagreeing about what counts as a move is worse than either rule
        // being imperfect. HbA1c runs 4.0-5.6, so half the width is 0.8.
        var moved = Find(Draws(MetricKeys.Hba1c, 5.2, 6.1), "metabolic_drift")
            .Components.First(c => c.Metric == MetricKeys.Hba1c);

        var didNot = Find(Draws(MetricKeys.Hba1c, 5.2, 5.7), "metabolic_drift")
            .Components.First(c => c.Metric == MetricKeys.Hba1c);

        Assert.Equal(Movement.Unfavourable, moved.Movement);
        Assert.Equal(Movement.Steady, didNot.Movement);
    }

    [Fact]
    public void AMetricWithNoGoodDirectionNeverCountsAsMoving()
    {
        // Lp(a) is inherited and effectively fixed, so a change in it is assay
        // variation. Nothing with a neutral polarity is allowed to vote, because the
        // only safe reading of "I do not know which way is bad" is that the change is
        // ordinary.
        var observations = Draws(MetricKeys.Hdl, 55, 54)
            .Concat(Draws(MetricKeys.TotalCholesterol, 180, 184));

        var pattern = Find(observations, "lipid_drift");

        Assert.Equal(0, pattern.Moving);
    }

    [Fact]
    public void FallingIsFavourableForSomethingWhereHigherIsWorse()
    {
        var pattern = Find(Draws(MetricKeys.Ldl, 160, 95), "lipid_drift");

        Assert.Equal(Movement.Favourable, pattern.Components.First(c => c.Metric == MetricKeys.Ldl).Movement);
        Assert.False(pattern.Fires);       // lipid_drift reads the unfavourable direction
    }

    [Fact]
    public void LabsThatOnlyEverCameFromADrawAreReadAsDrawsWhateverTheCatalogueSays()
    {
        // Glucose is catalogued as the medium tier because a home meter produces it
        // daily. Somebody whose only glucose comes off two blood draws a year has
        // sparse data wearing a medium label, and a trend test on it returns "too few
        // readings" when the right answer is "it moved this much".
        var pattern = Find(Draws(MetricKeys.Glucose, 88, 115), "metabolic_drift");
        var glucose = pattern.Components.First(c => c.Metric == MetricKeys.Glucose);

        Assert.Equal("against the previous draw", glucose.Basis);
        Assert.Equal(Movement.Unfavourable, glucose.Movement);
    }

    [Fact]
    public void ACertainTrendThatMovedNowhereIsNotMovement()
    {
        // Mann-Kendall tests whether a series is monotonic, not whether it went
        // anywhere. A resting heart rate creeping two tenths of a beat over ninety
        // days, with almost no noise around it, is as statistically certain as one
        // climbing ten beats -- and calling it "moving the wrong way" is true and
        // useless. Caught by running this against a seeded database rather than by a
        // test, which is why the floor exists at all.
        var observations = Ramp(MetricKeys.RestingHeartRate, 54, 54.2)
            .Concat(Ramp(MetricKeys.HrvRmssd, 68, 44));

        var pattern = Find(observations, "autonomic_strain");
        var rhr = pattern.Components.First(c => c.Metric == MetricKeys.RestingHeartRate);

        Assert.Equal(Movement.Steady, rhr.Movement);
        Assert.Contains("only by", rhr.Detail);
        Assert.Equal(1, pattern.Moving);
        Assert.False(pattern.Fires);
    }

    // ── What it produces ────────────────────────────────────────────────────────

    [Fact]
    public void APatternIsNeverSevereHoweverManyThingsAreMoving()
    {
        // Same rule as a lab result. A composite of slow drifts is a conversation to
        // have, not an emergency, and this combination is not a validated instrument
        // in the first place.
        var observations = Ramp(MetricKeys.WeightKg, 78, 90, days: 365, everyDays: 10, tier: Tiers.Medium)
            .Concat(Ramp(MetricKeys.WaistCircumferenceCm, 88, 105, days: 365, everyDays: 20, tier: Tiers.Medium))
            .Concat(Draws(MetricKeys.Triglycerides, 110, 320))
            .Concat(Draws(MetricKeys.FastingInsulin, 7, 28))
            .Concat(Draws(MetricKeys.Hba1c, 5.2, 6.4));

        var finding = Patterns.ToFinding(Find(observations, "metabolic_drift"), Today, favourable: false);

        Assert.Equal("notable", finding.Severity);
        Assert.NotEqual("high", finding.Severity);
        Assert.True(finding.Confidence <= 0.8);
    }

    [Fact]
    public void TheFindingSaysItIsNotADiagnosis()
    {
        var observations = Ramp(MetricKeys.WeightKg, 78, 84, days: 365, everyDays: 10, tier: Tiers.Medium)
            .Concat(Draws(MetricKeys.Triglycerides, 110, 190));

        var finding = Patterns.ToFinding(Find(observations, "metabolic_drift"), Today, favourable: false);

        Assert.Equal(FindingTypes.Pattern, finding.Type);
        Assert.Contains("not a diagnosis", finding.Summary);
        Assert.Contains("composite_pattern", finding.EvidenceJson ?? "");
    }

    [Fact]
    public void TheKeyIsStableAcrossRunsSoItCanBeDeduplicated()
    {
        var observations = Ramp(MetricKeys.WeightKg, 78, 84, days: 365, everyDays: 10, tier: Tiers.Medium)
            .Concat(Draws(MetricKeys.Triglycerides, 110, 190))
            .ToList();

        var first = Patterns.ToFinding(Find(observations, "metabolic_drift"), Today, false);
        var second = Patterns.ToFinding(Find(observations, "metabolic_drift"), Today.AddDays(1), false);

        Assert.Equal(first.Key, second.Key);
    }

    [Fact]
    public void EveryPatternIsEvaluatedEveryRunEvenWhenItSaysNothing()
    {
        // The negatives are served too. "No metabolic pattern, and three of these are
        // not being measured" is the sentence that tells somebody what to ask for, and
        // it cannot exist if silent patterns are dropped.
        var all = Patterns.Run([], Today, ReferenceRanges.Seed, "male", 44);

        Assert.Equal(5, all.Count);
        Assert.All(all, p => Assert.False(p.Fires));
        Assert.All(all, p => Assert.False(string.IsNullOrWhiteSpace(p.HeldBecause)));
        Assert.All(all, p => Assert.False(string.IsNullOrWhiteSpace(p.Interpretation)));
    }

    [Fact]
    public void EveryComponentNamedByAPatternExistsInTheCatalogue()
    {
        var all = Patterns.Run([], Today);

        foreach (var component in all.SelectMany(p => p.Components))
            Assert.NotNull(MetricCatalogue.Find(component.Metric));
    }
}
