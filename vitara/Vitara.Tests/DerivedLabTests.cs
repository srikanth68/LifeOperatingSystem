using Vitara.Domain.Health;

namespace Vitara.Tests;

// Three numbers a lab report usually leaves to the reader, and the much longer list of
// reasons not to produce them.
//
// Most of these tests are about refusing. That ratio is the point: the arithmetic is
// trivial and the damage is entirely in computing it on inputs that do not support it,
// because every one of those produces a confident, plausible, wrong number rather than
// an error anybody would notice.
public class DerivedLabTests
{
    private static Dictionary<string, double> Panel(params (string Metric, double Value)[] values) =>
        values.ToDictionary(v => v.Metric, v => v.Value);

    private static DerivedValue? Value(DerivedSet set, string metric) =>
        set.Values.FirstOrDefault(v => v.Metric == metric);

    private static DerivedRefusal? Refusal(DerivedSet set, string metric) =>
        set.Refusals.FirstOrDefault(r => r.Metric == metric);

    // ── Non-HDL ─────────────────────────────────────────────────────────────────

    [Fact]
    public void NonHdlIsTheSubtractionNobodyDoes()
    {
        var set = DerivedLabs.From(Panel(
            (MetricKeys.TotalCholesterol, 195),
            (MetricKeys.Hdl, 52)));

        var nonHdl = Value(set, MetricKeys.NonHdl);

        Assert.NotNull(nonHdl);
        Assert.Equal(143, nonHdl!.Value);
        Assert.Equal(Grade.A, nonHdl.Grade);
        Assert.Contains(MetricKeys.Hdl, nonHdl.From);
    }

    [Fact]
    public void NonHdlNeedsNoFastingState()
    {
        // The practical advantage of non-HDL over LDL, and a thing the code must not
        // accidentally throw away by applying one blanket fasting rule to everything.
        var set = DerivedLabs.From(
            Panel((MetricKeys.TotalCholesterol, 195), (MetricKeys.Hdl, 52)),
            fasting: false);

        Assert.NotNull(Value(set, MetricKeys.NonHdl));
    }

    [Fact]
    public void AnHdlAboveTheTotalIsRefusedRatherThanReturnedAsNegative()
    {
        // Cannot happen within one panel. When it does, the two values came from
        // different draws or one of them is in different units -- and a negative
        // non-HDL would be rendered as a wonderful result.
        var set = DerivedLabs.From(Panel(
            (MetricKeys.TotalCholesterol, 150),
            (MetricKeys.Hdl, 160)));

        Assert.Null(Value(set, MetricKeys.NonHdl));
        Assert.Contains("units", Refusal(set, MetricKeys.NonHdl)!.Reason);
    }

    [Fact]
    public void AMissingAnalyteIsNamedRatherThanJustAbsent()
    {
        var set = DerivedLabs.From(Panel((MetricKeys.TotalCholesterol, 195)));

        var refusal = Refusal(set, MetricKeys.NonHdl);

        Assert.NotNull(refusal);
        Assert.Contains(MetricKeys.Hdl, refusal!.Missing);
        Assert.Equal("Non-HDL cholesterol", refusal.Label);
    }

    // ── HOMA-IR ─────────────────────────────────────────────────────────────────

    [Fact]
    public void HomaIrIsComputedFromAFastingDraw()
    {
        var set = DerivedLabs.From(
            Panel((MetricKeys.Glucose, 95), (MetricKeys.FastingInsulin, 8)),
            fasting: true);

        var homa = Value(set, MetricKeys.HomaIr);

        Assert.NotNull(homa);
        Assert.Equal(1.88, homa!.Value);
        Assert.Equal(Grade.B, homa.Grade);
    }

    [Fact]
    public void HomaIrIsRefusedOnAFedDraw()
    {
        // The arithmetic works on post-meal insulin and the answer is nonsense: after
        // food insulin is several times its fasting level, so the index comes out
        // several times too high and reads as severe insulin resistance.
        var set = DerivedLabs.From(
            Panel((MetricKeys.Glucose, 110), (MetricKeys.FastingInsulin, 45)),
            fasting: false);

        Assert.Null(Value(set, MetricKeys.HomaIr));
        Assert.Contains("only defined on a fasting sample", Refusal(set, MetricKeys.HomaIr)!.Reason);
    }

    [Fact]
    public void AnUnrecordedFastingStateIsRefusedJustAsFirmly()
    {
        // "Probably fasting" is how a wrong number ships. Most draws with insulin on
        // them were fasting, which is exactly what makes assuming it tempting.
        var set = DerivedLabs.From(
            Panel((MetricKeys.Glucose, 95), (MetricKeys.FastingInsulin, 8)),
            fasting: null);

        Assert.Null(Value(set, MetricKeys.HomaIr));
        Assert.Contains("does not record whether it was fasting", Refusal(set, MetricKeys.HomaIr)!.Reason);
    }

    [Fact]
    public void APanelWithGlucoseAndNoInsulinSaysWhatToAskFor()
    {
        // The most valuable sentence in this file. Insulin resistance builds for years
        // while fasting glucose stays flat, so the common panel -- glucose, no insulin
        // -- is structurally blind to it. Reporting "no HOMA-IR" hides that; naming the
        // missing test is something to take to an appointment.
        var set = DerivedLabs.From(
            Panel((MetricKeys.Glucose, 95)),
            fasting: true);

        var refusal = Refusal(set, MetricKeys.HomaIr);

        Assert.NotNull(refusal);
        Assert.Contains(MetricKeys.FastingInsulin, refusal!.Missing);
        Assert.Contains("fasting insulin is the test to ask for", refusal.Reason);
    }

    [Fact]
    public void HomaIrCarriesTheAssayCaveatWithIt()
    {
        // Insulin assays are not standardised, so a value from one lab is not
        // comparable with one from another. Shipping the number without that attached
        // invites exactly the comparison that breaks it.
        var set = DerivedLabs.From(
            Panel((MetricKeys.Glucose, 95), (MetricKeys.FastingInsulin, 8)),
            fasting: true);

        var caveat = Value(set, MetricKeys.HomaIr)!.Caveat;

        Assert.Contains("not standardised", caveat);
        Assert.Contains("insulin therapy", caveat);
    }

    // ── eGFR ────────────────────────────────────────────────────────────────────

    [Fact]
    public void EgfrFollowsCkdEpi2021()
    {
        // Hand-checked: creatinine at kappa for a 50-year-old man leaves only the age
        // term, 142 x 0.9938^50.
        var set = DerivedLabs.From(
            Panel((MetricKeys.Creatinine, 0.9)), sex: "male", age: 50);

        Assert.Equal(104, Value(set, MetricKeys.Egfr)!.Value);
    }

    [Fact]
    public void TheFemaleCoefficientsAreUsedForAWoman()
    {
        // Different kappa, different alpha and a 1.012 multiplier. Applying the male
        // set to a woman understates her kidney function, which is a quiet error in
        // the direction of false reassurance.
        var set = DerivedLabs.From(
            Panel((MetricKeys.Creatinine, 0.7)), sex: "female", age: 50);

        Assert.Equal(105, Value(set, MetricKeys.Egfr)!.Value);
    }

    [Fact]
    public void AHigherCreatinineMeansALowerEstimate()
    {
        var good = DerivedLabs.From(Panel((MetricKeys.Creatinine, 0.9)), sex: "male", age: 50);
        var worse = DerivedLabs.From(Panel((MetricKeys.Creatinine, 1.6)), sex: "male", age: 50);

        Assert.True(Value(worse, MetricKeys.Egfr)!.Value < Value(good, MetricKeys.Egfr)!.Value);
    }

    [Fact]
    public void EgfrIsNotComputedWithAGuessedAge()
    {
        // Age moves this by tens of units. A default is not an estimate, it is a made-up
        // result wearing an equation's name.
        var set = DerivedLabs.From(Panel((MetricKeys.Creatinine, 0.9)), sex: "male");

        Assert.Null(Value(set, MetricKeys.Egfr));
        Assert.Contains("age", Refusal(set, MetricKeys.Egfr)!.Missing);
    }

    [Fact]
    public void EgfrIsNotComputedWithAGuessedSex()
    {
        var set = DerivedLabs.From(Panel((MetricKeys.Creatinine, 0.9)), age: 50);

        Assert.Null(Value(set, MetricKeys.Egfr));
        Assert.Contains("sex", Refusal(set, MetricKeys.Egfr)!.Missing);
    }

    [Fact]
    public void AnUnrecognisedSexDoesNotSilentlyBecomeMale()
    {
        var set = DerivedLabs.From(
            Panel((MetricKeys.Creatinine, 0.9)), sex: "prefer not to say", age: 50);

        Assert.Null(Value(set, MetricKeys.Egfr));
    }

    // ── Typing errors ───────────────────────────────────────────────────────────

    [Fact]
    public void AValueNoBodyCouldProduceIsTreatedAsATypo()
    {
        // 950 mg/dL of fasting glucose is not a reading, it is a stray keystroke. The
        // alternative is a HOMA-IR of 19 and a finding about a transcription error.
        var set = DerivedLabs.From(
            Panel((MetricKeys.Glucose, 950), (MetricKeys.FastingInsulin, 8)),
            fasting: true);

        Assert.Null(Value(set, MetricKeys.HomaIr));
        Assert.Contains("typing error", Refusal(set, MetricKeys.HomaIr)!.Reason);
    }

    // ── Across the whole draw ───────────────────────────────────────────────────

    [Fact]
    public void AFullPanelProducesAllThree()
    {
        var set = DerivedLabs.From(Panel(
                (MetricKeys.TotalCholesterol, 195),
                (MetricKeys.Hdl, 52),
                (MetricKeys.Glucose, 95),
                (MetricKeys.FastingInsulin, 8),
                (MetricKeys.Creatinine, 0.9)),
            fasting: true, sex: "male", age: 50);

        Assert.Equal(3, set.Values.Count);
        Assert.Empty(set.Refusals);
    }

    [Fact]
    public void AnEmptyPanelRefusesEverythingAndExplainsEachOne()
    {
        var set = DerivedLabs.From(Panel());

        Assert.Empty(set.Values);
        Assert.Equal(3, set.Refusals.Count);
        Assert.All(set.Refusals, r => Assert.False(string.IsNullOrWhiteSpace(r.Reason)));
        Assert.All(set.Refusals, r => Assert.False(string.IsNullOrWhiteSpace(r.Label)));
    }

    [Fact]
    public void EveryDerivedValueStatesTheArithmeticItUsed()
    {
        // A number whose derivation cannot be seen is one nobody can check against
        // their own report.
        var set = DerivedLabs.From(Panel(
                (MetricKeys.TotalCholesterol, 195),
                (MetricKeys.Hdl, 52),
                (MetricKeys.Glucose, 95),
                (MetricKeys.FastingInsulin, 8),
                (MetricKeys.Creatinine, 0.9)),
            fasting: true, sex: "male", age: 50);

        foreach (var value in set.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(value.Method), $"{value.Metric} does not say how");
            Assert.False(string.IsNullOrWhiteSpace(value.Caveat), $"{value.Metric} has no caveat");
            Assert.NotEmpty(value.From);
            Assert.Equal(Evidence.GradeFor(value.Metric), value.Grade);
        }
    }

    [Fact]
    public void EveryDerivedMetricIsInTheCatalogue()
    {
        foreach (var key in new[] { MetricKeys.NonHdl, MetricKeys.HomaIr, MetricKeys.Egfr })
        {
            var info = MetricCatalogue.Find(key);
            Assert.NotNull(info);
            Assert.True(info!.Computed, $"{key} is not marked computed");
        }
    }
}
