using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Insight.Health;

// One metric's direction, with the reason it is that and not something else.
public record TrajectoryResult(
    string Metric,
    string Label,
    string Tier,
    string State,
    string? Direction,          // up | down, when the series moved
    double? SlopePerMonth,
    double? RelativeChange,     // share of the median moved across the span
    double? PValue,
    int N,
    int WindowDays,
    DateOnly? FirstDay,
    DateOnly? LastDay,
    int? LastMeasuredDaysAgo,
    string? Grade,
    string Reason,
    // The Mann-Kendall variance, so a projection can build its interval from the same
    // test. Not part of what anyone reads.
    double Variance = 0);

public static class TrajectoryStates
{
    public const string Improving = "improving";
    public const string Worsening = "worsening";
    // For metrics with no better direction (steps, TSH): moving is not a judgement.
    public const string Rising = "rising";
    public const string Falling = "falling";
    public const string Stable = "stable";
    public const string Uncertain = "uncertain";
    public const string InsufficientData = "insufficient_data";

    public static readonly string[] All = [Improving, Worsening, Rising, Falling, Stable, Uncertain, InsufficientData];
}

// Getting better, getting worse, or holding (Vitara ADR 0018).
//
// The drift detector answers "is this moving enough to tell you about", at p < 0.01, and
// is silent otherwise. Silence then means four things -- steady, too noisy to say, not
// measured lately, never measured -- and every surface rendered all four the same way.
// This answers the other question, for every metric, in exactly one of five states and
// always with the reason.
//
// A label is not a finding. The bar here is p < 0.05 because a label is something a
// person looks at; the drift finding, which is something they are told, keeps its 0.01.
//
// Pure. Points in, one result out.
public static class Trajectory
{
    public const int DenseWindowDays = Patterns.DenseWindowDays;     // 90
    public const int MediumWindowDays = Patterns.MediumWindowDays;   // 365

    // Enough readings, spread over enough time, that a trend is about a period and not
    // a fortnight.
    public const int DenseMinReadings = 30;
    public const int DenseMinSpanDays = 28;
    public const int MediumMinReadings = 6;
    public const int MediumMinSpanDays = 60;

    // The label's significance level, two-sided 0.05.
    public const double LabelZ = 1.96;

    // One day of one metric. Several readings in a day are averaged by the caller.
    public record Point(DateOnly Day, double Value, Guid? LabPanelId = null);

    public static TrajectoryResult Compute(
        string metric,
        IReadOnlyList<Point> points,
        DateOnly asOf,
        IReadOnlyList<ReferenceRange>? ranges = null,
        string? sex = null,
        int? age = null,
        string? heldNote = null)
    {
        var info = MetricCatalogue.Find(metric);
        var label = info?.Label ?? metric;
        var tier = info?.Tier ?? Tiers.Dense;
        var grade = Evidence.GradeFor(metric)?.ToString();
        var polarity = MetricDirection.Polarity(metric);
        var staleAfter = info?.StaleAfterDays ?? 3;

        var ordered = points.Where(p => p.Day <= asOf).OrderBy(p => p.Day).ToList();

        // A lab-tier metric, or one whose only readings came off blood draws, is read
        // draw to draw. Never fitted: four points over two years is a sequence.
        var fromDraws = ordered.Any(p => p.LabPanelId is not null) && ordered.Count(p => p.LabPanelId is null) < 10;
        if (tier == Tiers.Sparse || fromDraws)
            return Sparse(metric, label, ordered, asOf, ranges, sex, age, grade, polarity);

        var window = tier == Tiers.Medium ? MediumWindowDays : DenseWindowDays;
        var minN = tier == Tiers.Medium ? MediumMinReadings : DenseMinReadings;
        var minSpan = tier == Tiers.Medium ? MediumMinSpanDays : DenseMinSpanDays;

        TrajectoryResult Result(string state, string reason, int n = 0, DateOnly? first = null, DateOnly? last = null,
            string? direction = null, double? perMonth = null, double? relative = null, double? p = null, double variance = 0) =>
            new(metric, label, tier, state, direction, perMonth, relative, p, n, window, first, last,
                ordered.Count == 0 ? null : asOf.DayNumber - ordered[^1].Day.DayNumber, grade,
                heldNote is null ? reason : $"{reason} {heldNote}", variance);

        if (ordered.Count == 0)
            return Result(TrajectoryStates.InsufficientData, "Never measured.");

        // Stopped, rather than steady -- the same rule the patterns use, so the two
        // cannot disagree about whether a series still describes the present.
        var ago = asOf.DayNumber - ordered[^1].Day.DayNumber;
        if (ago > staleAfter * Patterns.StaleWindowMultiple)
            return Result(TrajectoryStates.InsufficientData,
                $"Last measured {ago} days ago, so a trend would describe a period that has ended.");

        var inWindow = ordered.Where(p => p.Day > asOf.AddDays(-window)).ToList();
        var span = inWindow.Count == 0 ? 0 : inWindow[^1].Day.DayNumber - inWindow[0].Day.DayNumber;

        if (inWindow.Count < minN || span < minSpan)
            return Result(TrajectoryStates.InsufficientData,
                $"{Plural(inWindow.Count, "reading")} over {Plural(span, "day")} in the last {window} days; " +
                $"a direction needs at least {minN} readings across {minSpan} days.",
                inWindow.Count, inWindow.FirstOrDefault()?.Day, inWindow.LastOrDefault()?.Day);

        var series = inWindow.Select(p => (p.Day.DayNumber, p.Value)).ToList();
        if (Statistics.Trend(series, LabelZ) is not { } trend)
            return Result(TrajectoryStates.InsufficientData, "Too few readings for a trend test.", inWindow.Count);

        // How far the slope actually took the number, as a share of the number. Across
        // the span the readings cover, not the whole window: a 40-day series in a 90-day
        // window has not moved for 90 days.
        var typical = Math.Abs(Statistics.Median(inWindow.Select(p => p.Value).ToList()));
        var moved = Math.Abs(trend.SlopePerDay * span);
        var relative = typical > 1e-9 ? moved / typical : (double?)null;
        var bigEnough = relative is { } r && r >= Patterns.MinRelativeChange;
        var dir = trend.SlopePerDay > 0 ? "up" : "down";
        var perMonth = Math.Round(trend.SlopePerDay * 30, 3);
        var p = Math.Round(trend.PValue, 4);
        var pct = relative is { } rr ? $"{rr * 100:0.#}%" : "an unknown share";

        if (trend.IsSignificant && bigEnough)
        {
            var state = polarity == MetricDirection.Neutral
                ? (dir == "up" ? TrajectoryStates.Rising : TrajectoryStates.Falling)
                : (dir == "up") == (polarity == MetricDirection.HigherIsBetter)
                    ? TrajectoryStates.Improving
                    : TrajectoryStates.Worsening;

            return Result(state,
                $"{(dir == "up" ? "Up" : "Down")} about {Math.Abs(perMonth):0.##} a month over {Plural(inWindow.Count, "reading")} " +
                $"({pct} of its usual level, p = {p:0.###}).",
                inWindow.Count, inWindow[0].Day, inWindow[^1].Day, dir, perMonth, relative, p, trend.Variance);
        }

        if (!trend.IsSignificant && !bigEnough)
            return Result(TrajectoryStates.Stable,
                $"Moved {pct} of its usual level across {Plural(span, "day")}, with no consistent direction.",
                inWindow.Count, inWindow[0].Day, inWindow[^1].Day, null, perMonth, relative, p, trend.Variance);

        return Result(TrajectoryStates.Uncertain,
            trend.IsSignificant
                ? $"A consistent drift {dir}, but only {pct} of its usual level: real, and too small to call better or worse."
                : $"Moved {pct} of its usual level, but not consistently enough to rule out noise (p = {p:0.###}).",
            inWindow.Count, inWindow[0].Day, inWindow[^1].Day, dir, perMonth, relative, p, trend.Variance);
    }

    // Draw to draw, against the same bar the patterns use: half the printed range's
    // width or a quarter of the previous value, whichever is smaller.
    private static TrajectoryResult Sparse(
        string metric, string label, List<Point> ordered, DateOnly asOf,
        IReadOnlyList<ReferenceRange>? ranges, string? sex, int? age, string? grade, int polarity)
    {
        var draws = ordered
            .GroupBy(p => p.LabPanelId?.ToString() ?? p.Day.ToString("yyyy-MM-dd"))
            .Select(g => g.OrderByDescending(p => p.Day).First())
            .OrderBy(p => p.Day)
            .ToList();

        TrajectoryResult Result(string state, string reason, string? direction = null, double? relative = null) =>
            new(metric, label, Tiers.Sparse, state, direction, null, relative, null, draws.Count, 0,
                draws.FirstOrDefault()?.Day, draws.LastOrDefault()?.Day,
                draws.Count == 0 ? null : asOf.DayNumber - draws[^1].Day.DayNumber, grade, reason);

        if (draws.Count == 0) return Result(TrajectoryStates.InsufficientData, "Never measured.");
        if (draws.Count == 1)
            return Result(TrajectoryStates.InsufficientData,
                $"Measured once ({draws[0].Day:MMM yyyy}); a direction needs a second draw.");

        var latest = draws[^1];
        var previous = draws[^2];

        var ago = asOf.DayNumber - latest.Day.DayNumber;
        if (ago > Patterns.SparseStaleDays)
            return Result(TrajectoryStates.InsufficientData,
                $"Last drawn {latest.Day:MMM yyyy}, {ago} days ago: history rather than the present.");

        var shift = latest.Value - previous.Value;
        var bar = Patterns.LabMoveBar(previous.Value,
            ranges is null ? null : ReferenceRanges.For(ranges, metric, sex, age));
        var relative = Math.Abs(previous.Value) > 1e-9 ? Math.Abs(shift) / Math.Abs(previous.Value) : (double?)null;
        var detail = $"{Number(previous.Value)} ({previous.Day:MMM yyyy}) to {Number(latest.Value)} ({latest.Day:MMM yyyy})";

        if (Math.Abs(shift) < bar)
            return Result(TrajectoryStates.Stable, $"{detail}: inside the usual draw-to-draw variation.", null, relative);

        var dir = shift > 0 ? "up" : "down";
        var state = polarity == MetricDirection.Neutral
            ? (dir == "up" ? TrajectoryStates.Rising : TrajectoryStates.Falling)
            : (dir == "up") == (polarity == MetricDirection.HigherIsBetter)
                ? TrajectoryStates.Improving
                : TrajectoryStates.Worsening;

        return Result(state, $"{detail}. Low confidence: two draws are a comparison, not a trend.", dir, relative);
    }

    // Every catalogue metric, including the ones with nothing behind them: a row that
    // says "never measured" is the answer, an absent row is not.
    //
    // The baseline's exclusions apply -- illness and injury periods, travel for the
    // circadian metrics, readings from before the latest device -- because a trend that
    // runs through a fortnight of flu or across a ring swap is describing those, not the
    // person. A HELD step is deliberately not excluded: the hold freezes what counts as
    // normal, and the trajectory is exactly where the movement since must stay visible.
    public static List<TrajectoryResult> ForCatalogue(
        IReadOnlyList<Observation> observations,
        IReadOnlyList<DerivedMetric> derived,
        DateOnly asOf,
        IReadOnlyList<ExcludedPeriod>? excluded = null,
        IReadOnlyList<TravelPeriod>? travel = null,
        IReadOnlyList<Device>? devices = null,
        IReadOnlyList<RegimeHold>? holds = null,
        IReadOnlyList<ReferenceRange>? ranges = null,
        string? sex = null,
        int? age = null)
    {
        var latestDevice = (devices ?? [])
            .Where(d => d.ActiveFromLocal <= asOf)
            .Select(d => (DateOnly?)d.ActiveFromLocal)
            .DefaultIfEmpty(null)
            .Max();

        bool Keep(string metric, DateOnly day) =>
            !(excluded ?? []).Any(e => e.ExcludeFromBaseline && day >= e.StartLocal && day <= e.EndLocal)
            && !(BaselineCalculator.CircadianMetrics.Contains(metric)
                 && (travel ?? []).Any(t => day >= t.StartLocal && day <= t.EndLocal))
            && !(latestDevice is { } dev && day < dev && MetricCatalogue.Find(metric)?.Source == "ring");

        var byMetric = observations
            .Where(o => Keep(o.Metric, o.ObservedDateLocal))
            .GroupBy(o => o.Metric)
            .ToDictionary(g => g.Key, g => g
                .GroupBy(o => (o.ObservedDateLocal, o.LabPanelId))
                .Select(d => new Point(d.Key.ObservedDateLocal, d.Average(o => o.Value), d.Key.LabPanelId))
                .ToList());

        var computed = derived
            .GroupBy(d => d.Metric)
            .ToDictionary(g => g.Key, g => g.Select(d => new Point(d.ObservedDateLocal, d.Value)).ToList());

        return MetricCatalogue.All.Select(info =>
        {
            var points = info.Computed
                ? computed.GetValueOrDefault(info.Key) ?? byMetric.GetValueOrDefault(info.Key) ?? []
                : byMetric.GetValueOrDefault(info.Key) ?? [];

            var hold = (holds ?? []).FirstOrDefault(h => h.IsOpen && h.Metric == info.Key);
            var note = hold is null
                ? null
                : $"Includes a step since {hold.ChangePointLocal:d MMM} that is being held: your normal is still the earlier level.";

            return Compute(info.Key, points, asOf, ranges, sex, age, note);
        }).ToList();
    }

    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    private static string Number(double v) =>
        Math.Abs(v) >= 100 ? v.ToString("0") : Math.Abs(v) >= 10 ? v.ToString("0.#") : v.ToString("0.##");
}
