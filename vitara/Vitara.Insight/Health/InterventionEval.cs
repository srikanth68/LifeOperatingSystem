using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Insight.Health;

// A stretch of days, summarised.
public record EvalWindow(DateOnly From, DateOnly To, int N, double Median, double P25, double P75);

// How much of the change would probably have happened anyway.
//
// THE MOST IMPORTANT NUMBER IN THIS FILE and the one nothing else here would work
// without. See the long note on Rebound() below.
public record ReboundEstimate(int ComparableWindows, double Median, double P75, string Basis);

// Something else that moved. Reported, and reported as not the thing being tested.
public record SideChange(string Metric, string Label, double Change, string Direction, string Detail);

public record Evaluation(
    Guid Id,
    string Name,
    string Kind,
    string? Dose,
    DateOnly StartedOn,
    DateOnly? EndedOn,
    int DaysIn,
    string? TargetMetric,
    string? TargetLabel,
    string Verdict,
    string Statement,
    EvalWindow? Before,
    EvalWindow? After,
    double? Change,
    double? ChangeVsUsual,
    ReboundEstimate? Rebound,
    IReadOnlyList<SideChange> AlsoChanged,
    IReadOnlyList<string> Caveats,
    string Confidence,
    DateOnly? EarliestVerdict);

public static class InterventionEval
{
    // What a verdict can be. Stated as constants because several of them are
    // deliberately not "it worked" or "it did not", and a caller that switches on
    // strings should be switching on these.
    public static class Verdicts
    {
        public const string NoTarget = "no_target";
        public const string TooEarly = "too_early";
        public const string NotEnoughData = "not_enough_data";
        public const string CannotSeparate = "cannot_separate";
        public const string NoDirection = "no_direction";
        public const string Improved = "improved";
        public const string Worsened = "worsened";
        public const string NoChange = "no_change";
        public const string WouldHaveAnyway = "would_have_anyway";
    }

    // Nothing acts instantly, and the first fortnight of anything is the fortnight
    // somebody is still working out how to do it. Measuring from day one mixes the
    // adjustment in with the effect and makes a slow-acting change look like it did
    // nothing.
    public const int RunInDays = 14;

    // How long on each side. Four weeks is the shortest window where a daily metric
    // has enough readings for the median to be stable and short enough that a verdict
    // arrives while the person still cares.
    public const int WindowDays = 28;

    // Readings needed in a window before it is summarised at all.
    public const int MinReadings = 14;

    public static List<Evaluation> Run(
        IReadOnlyList<Intervention> interventions,
        IReadOnlyList<Observation> observations,
        DateOnly asOf,
        IReadOnlyList<ExcludedPeriod>? excluded = null,
        IReadOnlyList<TravelPeriod>? travel = null) =>
        interventions
            .OrderByDescending(i => i.StartedOnLocal)
            .Select(i => Evaluate(i, interventions, observations, asOf, excluded ?? [], travel ?? []))
            .ToList();

    private static Evaluation Evaluate(
        Intervention intervention,
        IReadOnlyList<Intervention> all,
        IReadOnlyList<Observation> observations,
        DateOnly asOf,
        IReadOnlyList<ExcludedPeriod> excluded,
        IReadOnlyList<TravelPeriod> travel)
    {
        var start = intervention.StartedOnLocal;
        var stopped = intervention.EndedOnLocal;
        var through = stopped is { } e && e < asOf ? e : asOf;
        var daysIn = through.DayNumber - start.DayNumber;
        var caveats = new List<string>();

        Evaluation Bail(string verdict, string statement, DateOnly? earliest = null) => new(
            intervention.Id, intervention.Name, intervention.Kind, intervention.Dose,
            start, stopped, daysIn,
            intervention.TargetMetric,
            intervention.TargetMetric is { } m ? MetricCatalogue.Find(m)?.Label ?? m : null,
            verdict, statement, null, null, null, null, null, [], caveats, "none", earliest);

        // ── Was anything declared? ───────────────────────────────────────────────
        //
        // The target is recorded when the intervention starts, before any result is
        // visible. That ordering is the whole guard against the other way of lying
        // with this feature: look at thirty metrics afterwards, find the one that
        // improved, and call it the effect. One of thirty always improves.
        if (string.IsNullOrWhiteSpace(intervention.TargetMetric))
            return Bail(Verdicts.NoTarget,
                "Nothing was recorded as the thing this was meant to change, so there is nothing to check it " +
                "against. Picking a metric now and finding it improved would be choosing the answer after " +
                "seeing it — note the target when you start something, and this can be evaluated from then on.");

        var metric = intervention.TargetMetric!;
        var info = MetricCatalogue.Find(metric);
        var label = info?.Label ?? metric;
        var polarity = MetricDirection.Polarity(metric);

        if (polarity == MetricDirection.Neutral)
            return Bail(Verdicts.NoDirection,
                $"{label} has no better or worse direction — it is read against a range rather than as " +
                "something to move, so \"did this improve it\" is not a question with an answer.");

        var earliest = start.AddDays(RunInDays + WindowDays);
        if (through < earliest)
            return Bail(Verdicts.TooEarly,
                $"Started {Days(daysIn)} ago. The first {RunInDays} days are not counted — nothing acts " +
                $"instantly and the first fortnight is mostly learning to do it — and {WindowDays} days are " +
                $"needed after that. A verdict is possible from {earliest:d MMM yyyy}.",
                earliest);

        // ── The two windows ──────────────────────────────────────────────────────
        var series = Daily(observations, metric);

        var before = Summarise(series, start.AddDays(-WindowDays), start.AddDays(-1));

        // Four weeks, matched to the before window and to the window the rebound is
        // measured over. Using "everything since it started" instead was the first
        // version and it compares a four-week before against a four-MONTH after, then
        // holds that up against rebounds measured over four weeks. Three different
        // window lengths in one comparison is not a comparison.
        var after = Summarise(series, start.AddDays(RunInDays),
            Min(through, start.AddDays(RunInDays + WindowDays - 1)));

        if (before is null || after is null)
            return Bail(Verdicts.NotEnoughData,
                before is null
                    ? $"There are fewer than {MinReadings} {label.ToLowerInvariant()} readings in the four weeks " +
                      "before this started, so there is nothing to compare against. A before is not " +
                      "reconstructable after the fact."
                    : $"There are fewer than {MinReadings} {label.ToLowerInvariant()} readings since this " +
                      "started. Measure more often and this becomes answerable.",
                earliest);

        // ── Confounders ──────────────────────────────────────────────────────────
        //
        // Two things started in the same month cannot be told apart, and this is the
        // single most common way a self-experiment produces a confident wrong answer.
        // Said rather than silently ignored, and said BEFORE the numbers, so the
        // numbers are read in its light.
        var overlapping = all
            .Where(o => o.Id != intervention.Id)
            .Where(o => Math.Abs(o.StartedOnLocal.DayNumber - start.DayNumber) <= WindowDays)
            .Select(o => o.Name)
            .ToList();

        if (overlapping.Count > 0)
            return Bail(Verdicts.CannotSeparate,
                $"{Join(overlapping)} started within four weeks of this one. Whatever changed, there is no " +
                "way to say which of them did it — and attributing it to one would be a guess dressed as a " +
                "result. Changing one thing at a time is the only way this question gets an answer.",
                earliest);

        foreach (var period in excluded.Where(p => Overlaps(p.StartLocal, p.EndLocal, start.AddDays(RunInDays), through)))
            caveats.Add($"{Days(period.EndLocal.DayNumber - period.StartLocal.DayNumber + 1)} of the period after " +
                        $"this started are marked as {period.Reason.Replace('_', ' ')}, which moves most of these " +
                        "numbers on its own.");

        foreach (var trip in travel.Where(p => Overlaps(p.StartLocal, p.EndLocal, start.AddDays(RunInDays), through)))
            caveats.Add($"You were away from {trip.StartLocal:d MMM} to {trip.EndLocal:d MMM}. Time zones and a " +
                        "different bed move sleep and recovery regardless of anything you started.");

        // ── What happened ────────────────────────────────────────────────────────
        var change = after.Median - before.Median;
        var better = polarity == MetricDirection.HigherIsBetter ? change : -change;

        // Against the long-run normal as well as against the four weeks before. These
        // answer different questions and the difference is the point: a return to your
        // own usual is not an improvement, however much better it feels than the bad
        // fortnight that prompted the change.
        var usual = Summarise(series, start.AddDays(-400), start.AddDays(-1), minimum: MinReadings * 2);
        var changeVsUsual = usual is null ? (double?)null : after.Median - usual.Median;

        // ── The rebound ──────────────────────────────────────────────────────────
        var rebound = Rebound(series, metric, before, start, polarity, all);

        // Noise floor: half the interquartile range of the before window. A move
        // smaller than the ordinary day-to-day spread is not a result.
        var noise = Math.Max(1e-9, (before.P75 - before.P25) / 2);

        string verdict;
        string statement;
        var confidence = "low";

        var direction = better > 0 ? "better" : "worse";
        var moved = $"{label} went from {Number(before.Median)} to {Number(after.Median)} " +
                    $"({Signed(change)}{UnitOf(info)}).";

        if (Math.Abs(better) < noise)
        {
            verdict = Verdicts.NoChange;
            statement = $"{moved} That is inside the ordinary spread of your own readings, so nothing moved " +
                        "that this can distinguish from an average month.";
        }
        else if (better < 0)
        {
            verdict = Verdicts.Worsened;
            statement = $"{moved} That is the wrong direction for what this was meant to do. " +
                        "Worth checking it is still the right thing to be doing.";
            confidence = rebound is null ? "low" : "moderate";
        }
        else if (rebound is { } r && better <= r.P75)
        {
            // THE VERDICT THIS FEATURE EXISTS TO BE ABLE TO GIVE.
            verdict = Verdicts.WouldHaveAnyway;
            statement = $"{moved} It is the right direction — but you started this after a below-par " +
                        $"stretch, and {r.Basis} a rebound of about {Number(r.Median)} from a stretch that bad, " +
                        "with no change made. This improvement is inside that, so it cannot be told apart from " +
                        "simply returning to normal.";
            confidence = "moderate";
        }
        else
        {
            verdict = Verdicts.Improved;
            statement = rebound is { } rb
                ? $"{moved} Bigger than the {Number(rb.Median)} your own history says to expect from " +
                  "simply rebounding off a stretch that bad, so something more than that happened."
                : $"{moved} There is not enough history to say how much of that would have happened anyway, " +
                  "so take the size with some salt.";
            confidence = rebound is null ? "low" : "moderate";
        }

        if (changeVsUsual is { } vu && verdict is Verdicts.Improved or Verdicts.WouldHaveAnyway)
        {
            var betterThanUsual = polarity == MetricDirection.HigherIsBetter ? vu : -vu;
            if (betterThanUsual < noise)
                caveats.Add($"Against your longer-run usual rather than the four weeks before, this sits " +
                            $"about where it always has. The improvement is relative to a bad patch.");
        }

        // Whether it stuck. The verdict is deliberately measured on weeks three to
        // six, which leaves everything since unexamined -- and for anything running
        // longer than that, "it worked and then stopped working" is a different
        // outcome from "it worked", and the more common one.
        if (through > start.AddDays(RunInDays + WindowDays * 2)
            && Summarise(series, through.AddDays(-WindowDays + 1), through) is { } latest
            && verdict is Verdicts.Improved or Verdicts.WouldHaveAnyway)
        {
            var heldBy = polarity == MetricDirection.HigherIsBetter
                ? latest.Median - before.Median
                : before.Median - latest.Median;

            caveats.Add(heldBy >= better * 0.5
                ? $"Still there in the last four weeks ({Number(latest.Median)}), {Days(daysIn)} in."
                : $"It did not hold: the last four weeks are back to {Number(latest.Median)}, close to where " +
                  "this started. The verdict above is measured on weeks three to six.");
        }

        if (stopped is { } ended)
            caveats.Add($"Stopped on {ended:d MMM yyyy}; everything above is measured up to then.");

        // ── What else moved ──────────────────────────────────────────────────────
        var alsoChanged = SideEffects(observations, metric, start, through, noiseScale: 0.75);

        return new Evaluation(
            intervention.Id, intervention.Name, intervention.Kind, intervention.Dose,
            start, stopped, daysIn, metric, label,
            verdict, statement, before, after,
            Math.Round(change, 2), changeVsUsual is { } c ? Math.Round(c, 2) : null,
            rebound, alsoChanged, caveats, confidence, earliest);
    }

    // ── How much would have happened anyway ─────────────────────────────────────
    //
    // Regression to the mean is the reason an evaluation feature cannot simply compare
    // before with after, and it is not a subtle statistical nicety here -- it is the
    // dominant effect, because of WHEN people start things.
    //
    // Nobody begins a sleep protocol during a good month. They begin it after two bad
    // weeks, which is to say they begin it at a low point selected precisely for being
    // low. The following fortnight is better whatever they do, because an unusually bad
    // stretch is usually followed by a more ordinary one. A before-and-after comparison
    // attributes that entirely to the intervention, every single time, for every
    // intervention. The feature then reports that everything works -- which is worse
    // than having no feature, because it launders noise into confidence.
    //
    // So: find the stretches in this person's own history that were as bad as the one
    // that prompted this, where nothing was started, and see what happened next. The
    // median of those is what "doing nothing" looks like from here. An improvement has
    // to beat it to count as an improvement.
    //
    // This is an estimate from a handful of windows and it is not a control group. It
    // is, however, the difference between a verdict that means something and one that
    // always says yes.
    private static ReboundEstimate? Rebound(
        IReadOnlyList<(DateOnly Day, double Value)> series,
        string metric,
        EvalWindow before,
        DateOnly start,
        int polarity,
        IReadOnlyList<Intervention> all)
    {
        var history = series.Where(p => p.Day < start.AddDays(-WindowDays)).ToList();
        if (history.Count < WindowDays * 2) return null;

        var longRun = Statistics.Median(history.Select(p => p.Value).ToList());

        // How far below par the pre-window was, in the adverse direction. If it was not
        // below par at all, there is no rebound to expect and no correction to make.
        var preGap = polarity == MetricDirection.HigherIsBetter
            ? longRun - before.Median
            : before.Median - longRun;

        if (preGap <= 0) return null;

        // How close a historical stretch has to be to count as "as bad as this one".
        //
        // THIS TOLERANCE IS LOAD-BEARING AND WAS NOT THERE AT FIRST. Demanding a
        // historical window be at least as far below par as the pre-window sounds
        // like the conservative choice. It is not: the pre-window's own median
        // wobbles by a point or two of noise, and when it lands slightly low, every
        // nominally identical dip in the person's history falls just short and is
        // thrown out. The comparable set drops below three, the rebound estimate
        // vanishes, and the verdict silently flips from "cannot be told apart from
        // normal recovery" to "improved".
        //
        // The guard disappearing because of noise is the single worst failure this
        // file could have, because it fails towards flattery and leaves no trace. So
        // "as bad as" means within half the spread of the window being matched.
        var tolerance = Math.Max(0, (before.P75 - before.P25) / 2);

        var rebounds = new List<double>();
        var first = history[0].Day;
        var last = history[^1].Day;

        // Scanned day by day, then skipped forward past a window that qualifies.
        //
        // Both halves matter. Stepping weekly instead was the first attempt and it
        // undercounted badly: a window has to line up with a bad stretch almost
        // exactly to be as far below par as the one being tested, and a seven-day
        // stride walks straight past most of those alignments. It found two episodes
        // in a history that plainly contained four.
        //
        // Daily stepping alone has the opposite fault -- consecutive windows share
        // twenty-seven of their twenty-eight days, so one bad month would arrive as
        // thirty "independent" examples and a handful of episodes would look like a
        // large sample. Skipping a full window on each hit counts each episode once.
        var day = first.AddDays(WindowDays);

        while (day.AddDays(RunInDays + WindowDays) <= last)
        {
            // Nothing was started near this window -- otherwise it is not an example of
            // what happens when you do nothing.
            if (all.Any(i => Math.Abs(i.StartedOnLocal.DayNumber - day.DayNumber) <= WindowDays))
            {
                day = day.AddDays(1);
                continue;
            }

            var candidate = Summarise(series, day.AddDays(-WindowDays), day.AddDays(-1), minimum: MinReadings / 2);
            var following = Summarise(series, day.AddDays(RunInDays), day.AddDays(RunInDays + WindowDays - 1),
                minimum: MinReadings / 2);

            if (candidate is null || following is null)
            {
                day = day.AddDays(1);
                continue;
            }

            var gap = polarity == MetricDirection.HigherIsBetter
                ? longRun - candidate.Median
                : candidate.Median - longRun;

            // As bad as, or worse than, the stretch that prompted this one. A window
            // only half as bad rebounds by less, and averaging it in would understate
            // what doing nothing achieves -- which biases every verdict towards
            // crediting the intervention. The strict comparison is the safe direction.
            if (gap < preGap - tolerance)
            {
                day = day.AddDays(1);
                continue;
            }

            var moved = following.Median - candidate.Median;
            rebounds.Add(polarity == MetricDirection.HigherIsBetter ? moved : -moved);

            day = day.AddDays(WindowDays);
        }

        // Three is not many. It is enough to say "this has happened before without you
        // doing anything", which is the claim being made, and the count travels with
        // the number so nobody reads it as more than it is.
        if (rebounds.Count < 3) return null;

        return new ReboundEstimate(
            rebounds.Count,
            Math.Round(Statistics.Median(rebounds), 2),
            Math.Round(Statistics.Percentile(rebounds, 0.75), 2),
            $"{rebounds.Count} earlier stretches of your own, just as far below par, recovered on their own by");
    }

    // ── Everything that was not being tested ────────────────────────────────────
    //
    // Reported, and reported as not the point. Something improving here is a lead to
    // follow by declaring it as the target of the next thing, not a result -- the
    // whole reason the target is recorded up front is that a metric picked after the
    // fact is a metric picked because it moved.
    private static List<SideChange> SideEffects(
        IReadOnlyList<Observation> observations, string target, DateOnly start, DateOnly through, double noiseScale)
    {
        var watch = new[]
        {
            MetricKeys.RestingHeartRate, MetricKeys.HrvRmssd, MetricKeys.TotalSleepMinutes,
            MetricKeys.SleepEfficiency, MetricKeys.WeightKg, MetricKeys.Steps, MetricKeys.ReadinessScore,
        };

        var changes = new List<SideChange>();

        foreach (var metric in watch.Where(m => m != target))
        {
            var series = Daily(observations, metric);
            var before = Summarise(series, start.AddDays(-WindowDays), start.AddDays(-1));
            var after = Summarise(series, start.AddDays(RunInDays), through);
            if (before is null || after is null) continue;

            var change = after.Median - before.Median;
            var noise = Math.Max(1e-9, (before.P75 - before.P25) / 2) * noiseScale;
            if (Math.Abs(change) < noise) continue;

            var polarity = MetricDirection.Polarity(metric);
            var better = polarity == MetricDirection.HigherIsBetter ? change : -change;
            var info = MetricCatalogue.Find(metric);

            changes.Add(new SideChange(
                metric,
                info?.Label ?? metric,
                Math.Round(change, 2),
                polarity == MetricDirection.Neutral ? "changed" : better > 0 ? "better" : "worse",
                $"{Number(before.Median)} to {Number(after.Median)}{UnitOf(info)}"));
        }

        return changes;
    }

    // ── Shared ──────────────────────────────────────────────────────────────────

    private static List<(DateOnly Day, double Value)> Daily(
        IReadOnlyList<Observation> observations, string metric) =>
        observations
            .Where(o => o.Metric == metric)
            .GroupBy(o => o.ObservedDateLocal)
            .Select(g => (Day: g.Key, Value: g.Average(o => o.Value)))
            .OrderBy(p => p.Day)
            .ToList();

    // Median and quartiles rather than the mean, throughout. One terrible night inside
    // a four-week window moves a mean by enough to decide a verdict, and these windows
    // are short enough that it would.
    private static EvalWindow? Summarise(
        IReadOnlyList<(DateOnly Day, double Value)> series, DateOnly from, DateOnly to, int? minimum = null)
    {
        var inside = series.Where(p => p.Day >= from && p.Day <= to).Select(p => p.Value).ToList();
        if (inside.Count < (minimum ?? MinReadings)) return null;

        return new EvalWindow(from, to, inside.Count,
            Math.Round(Statistics.Median(inside), 2),
            Math.Round(Statistics.Percentile(inside, 0.25), 2),
            Math.Round(Statistics.Percentile(inside, 0.75), 2));
    }

    private static bool Overlaps(DateOnly aFrom, DateOnly aTo, DateOnly bFrom, DateOnly bTo) =>
        aFrom <= bTo && bFrom <= aTo;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static string Days(int n) => n == 1 ? "1 day" : $"{n} days";

    private static string Join(List<string> names) => names.Count == 1
        ? names[0]
        : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

    private static string Number(double v) =>
        v == Math.Floor(v) ? ((long)v).ToString() : v.ToString("0.##");

    private static string Signed(double v) => (v > 0 ? "+" : "") + Number(v);

    private static string UnitOf(MetricInfo? info) =>
        string.IsNullOrWhiteSpace(info?.Unit) ? "" : " " + info!.Unit;
}
