using Vitara.Domain.Health;

namespace Vitara.Tests;

// What to strive for (Vitara ADR 0028). The numbers are checked against their sources here,
// so a careless edit to a range fails a test rather than quietly changing advice.
public class HealthyRangesTests
{
    private static HealthyRange R(string metric, int? age = 45, string? sex = "male") =>
        HealthyRanges.For(age, sex).Ranges.Single(r => r.Metric == metric);

    [Fact]
    public void EveryRangeNamesItsSourceAndAGrade()
    {
        var (ranges, _) = HealthyRanges.For(45, "female");
        Assert.All(ranges, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Source));
            Assert.False(string.IsNullOrWhiteSpace(r.Basis));
            Assert.Contains(r.Grade, new[] { "A", "B", "C" });
            Assert.True(r.Low is not null || r.High is not null, r.Metric);
        });
        // Exactly one range per measure.
        Assert.Equal(ranges.Count, ranges.Select(r => r.Metric).Distinct().Count());
    }

    [Fact]
    public void Vo2MaxIsTheFriendTopQuarterForAgeAndSex()
    {
        Assert.Equal(45.0, R(MetricKeys.Vo2Max, 45, "male").Low);        // FRIEND men 40-49, 75th
        Assert.Equal(31.9, R(MetricKeys.Vo2Max, 45, "male").RiskBelow);  // 25th
        Assert.Equal(27.6, R(MetricKeys.Vo2Max, 52, "f").Low);           // women 50-59
        Assert.Equal(55.2, R(MetricKeys.Vo2Max, 19, "male").Low);        // under 20 reads the 20s row
        Assert.Equal(30.4, R(MetricKeys.Vo2Max, 85, "male").Low);        // over 79 reads the 70s row
    }

    [Fact]
    public void WhatNeedsAgeOrSexIsMissingWithAReason_NotGuessed()
    {
        var (ranges, missing) = HealthyRanges.For(null, null);
        foreach (var m in new[] { MetricKeys.Vo2Max, MetricKeys.WaistCircumferenceCm, MetricKeys.GripStrengthKg, MetricKeys.Hdl })
        {
            Assert.DoesNotContain(ranges, r => r.Metric == m);
            Assert.Contains(missing, x => x.Metric == m && x.Reason.Contains("profile"));
        }
        Assert.Contains(ranges, r => r.Metric == MetricKeys.Steps);    // falls back to the under-60 count
    }

    [Fact]
    public void StepsLevelOffEarlierFromSixty()
    {
        Assert.Equal(8000, R(MetricKeys.Steps, 45).Low);
        Assert.Equal(6000, R(MetricKeys.Steps, 64).Low);
        Assert.Null(R(MetricKeys.Steps, 45).High);   // more is not harmful
    }

    [Fact]
    public void SouthAsianWaistCutOffs()
    {
        Assert.Equal(90, R(MetricKeys.WaistCircumferenceCm, 40, "male").High);
        Assert.Equal(80, R(MetricKeys.WaistCircumferenceCm, 40, "female").High);
    }

    [Theory]
    [InlineData(MetricKeys.SystolicBp, 118, "inside", false)]
    [InlineData(MetricKeys.SystolicBp, 125, "above", false)]   // above the aim, under the risk line
    [InlineData(MetricKeys.SystolicBp, 130, "above", true)]    // "130 and over"
    [InlineData(MetricKeys.Ldl, 99.5, "inside", false)]
    [InlineData(MetricKeys.Ldl, 100, "above", false)]          // "under 100"
    [InlineData(MetricKeys.RestingHeartRate, 48, "below", false)]
    [InlineData(MetricKeys.RestingHeartRate, 80, "above", true)]
    [InlineData(MetricKeys.Steps, 4200, "below", true)]
    [InlineData(MetricKeys.Steps, 15000, "inside", false)]
    public void ReadingsAreReadAgainstTheRangeAndTheRiskLine(string metric, double value, string position, bool atRisk)
    {
        var r = R(metric);
        Assert.Equal(position, HealthyRanges.Position(r, value));
        Assert.Equal(atRisk, HealthyRanges.AtRisk(r, value));
    }

    [Fact]
    public void PersonalOnlyMeasuresGetNoPopulationTarget()
    {
        var (ranges, _) = HealthyRanges.For(45, "male");
        Assert.All(HealthyRanges.PersonalOnly, m => Assert.DoesNotContain(ranges, r => r.Metric == m));
    }
}
