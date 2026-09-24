using Vitara.Insight.Health;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Tests;

// The one metric that arrives already baselined.
//
// The ring reports a deviation from its own long-run reference, and Vitara baselines
// that deviation again. That is deliberate -- it cancels any standing offset -- but it
// means the z-score can be large when nothing moved and small when something did, and
// neither failure announces itself. These tests pin the two absolutes that bracket it.
public class SkinTemperatureTests
{
    private static readonly DateOnly Today = new(2026, 9, 24);

    // floor 0.15 C, override 0.50 C
    private static readonly HealthThresholdSet T = new(1.5, -1.5, 1.5, 0.15, 0.50);

    private static DailyVitals Day(int daysAgo, double? rhr, double? hrv, double? tempZ, double? tempC = null) =>
        new(Today.AddDays(-daysAgo), rhr, hrv, tempZ, tempC);

    // ── The floor ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_wobble_smaller_than_the_sensor_can_resolve_does_not_count()
    {
        // 2.5 SD sounds decisive; five hundredths of a degree is not a fever.
        Assert.False(FindingDetectors.TemperatureCounts(z: 2.5, degrees: 0.05, T));
    }

    [Fact]
    public void Unusual_and_actually_warm_counts()
    {
        Assert.True(FindingDetectors.TemperatureCounts(z: 1.8, degrees: 0.30, T));
    }

    [Fact]
    public void Warm_but_not_unusual_does_not_count_on_its_own()
    {
        // Someone who often runs a fifth of a degree warm is not ill every other week.
        Assert.False(FindingDetectors.TemperatureCounts(z: 0.4, degrees: 0.20, T));
    }

    // ── The override ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_large_rise_counts_even_when_the_z_score_has_nothing_to_see()
    {
        // The drifting-reference case: the ring's own baseline creeps up with the user,
        // the reported deviation stays unremarkable, and half a degree is half a degree.
        Assert.True(FindingDetectors.TemperatureCounts(z: 0.2, degrees: 0.60, T));
    }

    // ── No degrees at all ────────────────────────────────────────────────────────

    [Fact]
    public void Without_degrees_the_z_score_still_decides()
    {
        // An imported history with no raw values must stay readable. Dropping the
        // component silently would be worse than scoring it on what is there.
        Assert.True(FindingDetectors.TemperatureCounts(z: 1.8, degrees: null, T));
        Assert.False(FindingDetectors.TemperatureCounts(z: 0.4, degrees: null, T));
    }

    [Fact]
    public void Nothing_at_all_does_not_count()
    {
        Assert.False(FindingDetectors.TemperatureCounts(z: null, degrees: null, T));
    }

    // ── Through the illness detector ─────────────────────────────────────────────

    [Fact]
    public void A_trivial_warm_reading_cannot_be_the_second_of_three()
    {
        // Resting HR is up, HRV is flat, and temperature is 2 SD on a movement of three
        // hundredths of a degree. Before the floor this was a notable illness finding.
        var finding = FindingDetectors.EarlyIllness(
            [Day(1, 1.8, -0.2, 2.0, 0.03), Day(0, 1.9, -0.1, 2.1, 0.04)], T, 2);

        Assert.Null(finding);
    }

    [Fact]
    public void A_real_warm_reading_still_is()
    {
        var finding = FindingDetectors.EarlyIllness(
            [Day(1, 1.8, -0.2, 2.0, 0.28), Day(0, 1.9, -0.1, 2.1, 0.33)], T, 2);

        Assert.NotNull(finding);
        Assert.Equal("notable", finding!.Severity);
    }

    [Fact]
    public void The_evidence_records_degrees_beside_the_z_score()
    {
        var finding = FindingDetectors.EarlyIllness(
            [Day(1, 1.8, -1.7, 1.6, 0.31), Day(0, 1.9, -1.9, 1.8, 0.35)], T, 2);

        Assert.Contains("skinTempC", finding!.EvidenceJson);
        Assert.Contains("skinTempCounted", finding.EvidenceJson);
    }

    // ── And through a whole run ──────────────────────────────────────────────────

    private static Baseline Valid(string metric) => new()
    {
        Metric = metric, ComputedOnLocal = Today, Mean = 0, StdDev = 0.05, N = 60, IsValid = true,
    };

    private static DerivedMetric Z(string metric, int daysAgo, double z) => new()
    {
        Metric = $"{metric}_z", ObservedDateLocal = Today.AddDays(-daysAgo), Value = z,
    };

    private static Observation Obs(string metric, int daysAgo, double value) => new()
    {
        Metric = metric, ObservedDateLocal = Today.AddDays(-daysAgo), Value = value, Unit = "C",
    };

    [Fact]
    public void A_standalone_deviation_finding_is_held_to_the_same_floor()
    {
        // Otherwise the wobble the illness detector rejected arrives anyway, as its own
        // finding, saying the same thing about the same three hundredths of a degree.
        var input = new FindingRunInputs(
            [Obs(MetricKeys.SkinTempDeviation, 1, 0.04), Obs(MetricKeys.SkinTempDeviation, 0, 0.03)],
            [Valid(MetricKeys.SkinTempDeviation)],
            [Z(MetricKeys.SkinTempDeviation, 1, 2.4), Z(MetricKeys.SkinTempDeviation, 0, 2.6)],
            Today);

        var findings = FindingRun.Detect(input);

        Assert.DoesNotContain(findings, f =>
            f.Type == FindingTypes.Deviation && f.Metric == MetricKeys.SkinTempDeviation);
    }

    [Fact]
    public void A_real_rise_still_produces_one()
    {
        var input = new FindingRunInputs(
            [Obs(MetricKeys.SkinTempDeviation, 1, 0.32), Obs(MetricKeys.SkinTempDeviation, 0, 0.36)],
            [Valid(MetricKeys.SkinTempDeviation)],
            [Z(MetricKeys.SkinTempDeviation, 1, 2.4), Z(MetricKeys.SkinTempDeviation, 0, 2.6)],
            Today);

        var findings = FindingRun.Detect(input);

        Assert.Contains(findings, f =>
            f.Type == FindingTypes.Deviation && f.Metric == MetricKeys.SkinTempDeviation);
    }

    [Fact]
    public void A_cold_reading_is_not_held_to_the_warm_floor()
    {
        // The floor guards the high side, which is the side illness shares. Running
        // colder than usual is a real reading and is still reported.
        var input = new FindingRunInputs(
            [Obs(MetricKeys.SkinTempDeviation, 1, -0.30), Obs(MetricKeys.SkinTempDeviation, 0, -0.34)],
            [Valid(MetricKeys.SkinTempDeviation)],
            [Z(MetricKeys.SkinTempDeviation, 1, -2.4), Z(MetricKeys.SkinTempDeviation, 0, -2.6)],
            Today);

        var findings = FindingRun.Detect(input);

        Assert.Contains(findings, f =>
            f.Type == FindingTypes.Deviation && f.Metric == MetricKeys.SkinTempDeviation && f.Direction == "low");
    }
}
