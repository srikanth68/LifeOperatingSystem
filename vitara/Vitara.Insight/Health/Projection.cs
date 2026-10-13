using Vitara.Domain.Health;

namespace Vitara.Insight.Health;

public record ProjectionResult(
    string Metric,
    string Label,
    bool Projected,
    int HorizonDays,
    double? CurrentLevel,
    double? ProjectedLow,
    double? ProjectedHigh,
    string? Direction,
    string Basis,
    string Grade,
    string Caveat,
    string? Refusal,
    string? HorizonNote);

// "If this continues", as a range or a refusal (Vitara ADR 0021).
//
// The forecast covers tomorrow. People also ask where a metric is heading over months,
// and a fitted time-series model on one person's year would look far more certain than
// it is. A slope with an honest interval is the most this data supports: the Theil-Sen
// slope with Sen's rank-based interval, built from the same Mann-Kendall variance the
// trajectory already computed, so the two cannot disagree about whether it is moving.
//
// Pure. Points and the trajectory in, a range or a refusal out.
public static class Projection
{
    public const int MaxHorizonDays = 180;
    public const string GradeLabel = "C";

    private const string Caveat =
        "A straight line drawn forward from the recent past. Bodies do not move in straight lines, and anything you " +
        "change from today changes this. It is a range of where the recent pattern points, not a prediction.";

    // Values a projected line may not cross. A linear extrapolation runs happily past
    // what a body can do; a sleep score of 112 is not a projection, it is a bug.
    private static (double Low, double High) Plausible(string metric) => metric switch
    {
        MetricKeys.SleepScore or MetricKeys.ReadinessScore or MetricKeys.ActivityScore => (0, 100),
        MetricKeys.Spo2Average or MetricKeys.SleepEfficiency or MetricKeys.BodyFatPct => (0, 100),
        MetricKeys.SkinTempDeviation => (-5, 5),
        MetricKeys.BedtimeMinutes or MetricKeys.WakeMinutes => (-720, 720),
        MetricKeys.RestingHeartRate => (25, 150),
        _ => (0, double.MaxValue),
    };

    public static ProjectionResult Compute(
        string metric,
        IReadOnlyList<Trajectory.Point> points,
        TrajectoryResult trajectory,
        int horizonDays)
    {
        var info = MetricCatalogue.Find(metric);
        var label = info?.Label ?? metric;

        ProjectionResult Refuse(string why) =>
            new(metric, label, false, horizonDays, null, null, null, null, "", GradeLabel, Caveat, why, null);

        if ((info?.Tier ?? trajectory.Tier) == Tiers.Sparse)
            return Refuse($"{label} is a lab value. Two or four draws a year are a sequence to compare, never a line to extend.");

        if (trajectory.State is not (TrajectoryStates.Improving or TrajectoryStates.Worsening
                                  or TrajectoryStates.Rising or TrajectoryStates.Falling))
            return Refuse(trajectory.State switch
            {
                TrajectoryStates.Stable => $"{label} is stable, so there is no direction to continue.",
                TrajectoryStates.Uncertain => $"{label}'s direction is uncertain: {trajectory.Reason}",
                _ => $"Not enough data: {trajectory.Reason}",
            });

        if (trajectory.FirstDay is not { } first || trajectory.LastDay is not { } last)
            return Refuse("Not enough data to draw a line.");

        var series = points
            .Where(p => p.Day >= first && p.Day <= last)
            .Select(p => (p.Day.DayNumber, p.Value))
            .ToList();

        if (Statistics.TheilSenSlope(series) is not { } slope
            || Statistics.TheilSenInterval(series, trajectory.Variance, Trajectory.LabelZ) is not { } interval)
            return Refuse("Not enough data to draw a line.");

        // The interval straddles zero: the slope could be either way, and any number would
        // claim a direction the data does not have.
        if (interval.Low < 0 && interval.High > 0)
            return Refuse($"The range of plausible slopes for {Lower(label)} includes no change at all, so the direction is not settled.");

        var span = last.DayNumber - first.DayNumber;
        var horizon = Math.Clamp(horizonDays, 1, MaxHorizonDays);
        string? note = horizon < horizonDays ? $"Capped at {MaxHorizonDays} days." : null;
        if (horizon > span / 2)
        {
            horizon = Math.Max(1, span / 2);
            note = $"Capped at {horizon} days: no further ahead than half the {span} days behind it.";
        }

        // The two interval slopes pivot through the series' median point (Conover's
        // intercept), not through the last day. Anchored at the end, the range carried
        // only the slope's uncertainty and none of the level's, and came out narrower than
        // the truth. Pivoting at the centre, the fan widens with distance from where the
        // data actually is, which is the honest shape.
        var centreDay = Statistics.Median(series.Select(p => (double)p.DayNumber).ToList());
        var centreValue = Statistics.Median(series.Select(p => p.Value).ToList());
        var current = centreValue + slope * (last.DayNumber - centreDay);
        var ahead = last.DayNumber + horizon - centreDay;

        var (lo, hi) = Plausible(metric);
        var a = Math.Clamp(centreValue + interval.Low * ahead, lo, hi);
        var b = Math.Clamp(centreValue + interval.High * ahead, lo, hi);
        var decimals = (info?.Decimals ?? 1) + 1;

        return new ProjectionResult(
            metric, label, true, horizon,
            Math.Round(current, decimals),
            Math.Round(Math.Min(a, b), decimals),
            Math.Round(Math.Max(a, b), decimals),
            slope > 0 ? "up" : "down",
            $"If the last {span} days' pattern continued for {horizon} more days.",
            GradeLabel, Caveat, null, note);
    }

    private static string Lower(string s) =>
        s.Length > 1 && char.IsUpper(s[0]) && !char.IsUpper(s[1]) ? char.ToLowerInvariant(s[0]) + s[1..] : s;
}
