using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Insight.Health;

// One metric, and how much of it there actually is.
public record MetricCoverage(
    string Metric,
    string Label,
    string Group,
    string Tier,
    string Source,
    int Readings,
    DateOnly? First,
    DateOnly? Last,
    int? DaysSinceLast,
    int SpanDays,
    string Baseline,        // ready | learning | none
    string State);          // current | stale | never

// One thing this system can do, and whether it can currently do it.
public record Capability(
    string Key,
    string Group,
    string Label,
    string State,                       // speaking | quiet | blind
    string Says,
    IReadOnlyList<string> Needs,
    int EverSaid,                       // times this has opened a finding, across the whole ledger
    string? Grade);

public record SelfCheckResult(
    DateOnly AsOf,
    int Speaking,
    int Quiet,
    int Blind,
    IReadOnlyList<string> Headline,
    IReadOnlyList<MetricCoverage> Coverage,
    IReadOnlyList<Capability> Capabilities);

// What does this system actually have to say about this body?
//
// Every other surface here answers a health question. This one answers a question
// about the software, and it is the only honest way to find out whether the analysis
// layer is doing anything at all -- because the two failure modes that matter are
// indistinguishable from the outside.
//
// A detector that reports nothing might be reporting that nothing is happening, which
// is good news. It might equally be reporting that it cannot see: no valid baseline,
// no second lab draw, a metric nobody has measured since August. Those are opposite
// facts and every existing surface renders them the same way -- as silence.
//
// THE DISTINCTION IS THE WHOLE POINT. Three states, never two:
//
//   speaking -- it ran and found something
//   quiet    -- it ran, with enough data to have found something, and did not
//   blind    -- it could not run, and here is what would unblock it
//
// There is deliberately NO SINGLE SCORE. "Vitara is 62% operational" is exactly the
// kind of summary number this system grades as experimental everywhere else, and
// producing one here would be the software failing its own test on the page that
// exists to test it. Counts and a list, nothing more.
//
// Two sources of truth, kept separate because they answer different questions. What a
// detector has EVER said comes from the findings ledger -- the record of what this
// system has actually told its user. What it CAN say now is computed live. A detector
// that has fired a hundred times and is now blind is a different situation from one
// that has never fired and is ready.
public static class SelfCheck
{
    public const string Speaking = "speaking";
    public const string Quiet = "quiet";
    public const string Blind = "blind";

    public record Inputs(
        IReadOnlyList<Observation> Observations,
        IReadOnlyList<DerivedMetric> Derived,
        IReadOnlyList<Baseline> Baselines,
        IReadOnlyList<Finding> Findings,            // every finding, resolved ones included
        IReadOnlyList<Intervention> Interventions,
        IReadOnlyList<Prediction.DayRow> Days,
        IReadOnlyList<ReferenceRange> Ranges,
        DateOnly AsOf,
        string? Sex = null,
        int? Age = null);

    public static SelfCheckResult Run(Inputs input)
    {
        var coverage = Coverage(input);
        var capabilities = new List<Capability>();

        capabilities.Add(Baselines(input, coverage));
        capabilities.AddRange(Detectors(input, coverage));
        capabilities.AddRange(PatternRows(input));
        capabilities.Add(Relationships(input));
        capabilities.AddRange(Forecasts(input));
        capabilities.Add(InterventionRow(input));
        capabilities.Add(LabRow(input, coverage));

        return new SelfCheckResult(
            input.AsOf,
            capabilities.Count(c => c.State == Speaking),
            capabilities.Count(c => c.State == Quiet),
            capabilities.Count(c => c.State == Blind),
            Headline(input, coverage, capabilities),
            coverage,
            capabilities);
    }

    // ── Coverage ────────────────────────────────────────────────────────────────

    private static List<MetricCoverage> Coverage(Inputs input)
    {
        var byMetric = input.Observations.GroupBy(o => o.Metric).ToDictionary(g => g.Key, g => g.ToList());
        var derivedByMetric = input.Derived.GroupBy(d => d.Metric).ToDictionary(g => g.Key, g => g.ToList());

        return MetricCatalogue.All.Select(info =>
        {
            var days = info.Computed
                ? derivedByMetric.GetValueOrDefault(info.Key)?.Select(d => d.ObservedDateLocal).ToList() ?? []
                : byMetric.GetValueOrDefault(info.Key)?.Select(o => o.ObservedDateLocal).ToList() ?? [];

            var first = days.Count == 0 ? (DateOnly?)null : days.Min();
            var last = days.Count == 0 ? (DateOnly?)null : days.Max();
            var since = last is { } l ? input.AsOf.DayNumber - l.DayNumber : (int?)null;

            // The best-supported bucket where a metric splits by context, matching what
            // the metrics page shows, so the two cannot disagree about readiness.
            var baseline = input.Baselines
                .Where(b => b.Metric == info.Key)
                .OrderByDescending(b => b.N)
                .FirstOrDefault();

            return new MetricCoverage(
                info.Key, info.Label, info.Group, info.Tier, info.Source,
                days.Count, first, last, since,
                first is { } f && last is { } t ? t.DayNumber - f.DayNumber : 0,
                baseline is null ? "none" : baseline.IsValid ? "ready" : "learning",
                last is null ? "never" : since > info.StaleAfterDays ? "stale" : "current");
        }).ToList();
    }

    // ── Baselines ───────────────────────────────────────────────────────────────

    private static Capability Baselines(Inputs input, List<MetricCoverage> coverage)
    {
        // Only the metrics allowed to raise a deviation. A baseline on something that
        // never speaks is not a capability, it is a number in a table.
        var watched = coverage.Where(c => FindingRun.DeviationMetrics.Contains(c.Metric)).ToList();
        var ready = watched.Count(c => c.Baseline == "ready");
        var learning = watched.Count(c => c.Baseline == "learning");
        var none = watched.Count(c => c.Baseline == "none");

        var needs = watched
            .Where(c => c.Baseline != "ready")
            .Select(c => c.Baseline == "learning"
                ? $"{c.Label}: still learning"
                : $"{c.Label}: no readings to learn from")
            .ToList();

        return new Capability(
            "baselines", "Baselines", "What normal looks like for you",
            ready == 0 ? Blind : ready < watched.Count ? Quiet : Speaking,
            ready == 0
                ? $"No metric has a usable personal baseline yet. Until one does, nothing can be called unusual — {HealthThresholds.MinBaselineN} readings is the floor."
                : $"{ready} of {watched.Count} watched metrics have a usable baseline. {learning} still learning, {none} with nothing recorded.",
            needs,
            0,
            Evidence.GradeFor(Evidence.PersonalBaseline)?.ToString());
    }

    // ── Detectors ───────────────────────────────────────────────────────────────

    private static IEnumerable<Capability> Detectors(Inputs input, List<MetricCoverage> coverage)
    {
        int EverSaid(string type) => input.Findings.Count(f => f.Type == type);
        bool HasDerived(string metric) => input.Derived.Any(d => d.Metric == $"{metric}_z");

        var ready = coverage.Where(c => c.Baseline == "ready").Select(c => c.Metric).ToHashSet();

        // Deviation
        var deviationReady = FindingRun.DeviationMetrics.Where(m => ready.Contains(m)).ToList();
        yield return Detector(
            "detector:deviation", "Unusual for you, two days running",
            deviationReady.Count > 0,
            deviationReady.Count > 0
                ? $"Watching {deviationReady.Count} of {FindingRun.DeviationMetrics.Length} metrics."
                : "No metric has a usable baseline, so nothing can be outside one.",
            FindingRun.DeviationMetrics.Where(m => !ready.Contains(m))
                .Select(m => $"{Label(coverage, m)}: needs a settled baseline").ToList(),
            EverSaid(FindingTypes.Deviation), null);

        // Early illness
        var illnessParts = new[] { MetricKeys.RestingHeartRate, MetricKeys.HrvRmssd, MetricKeys.SkinTempDeviation };
        var illnessHave = illnessParts.Where(HasDerived).ToList();
        yield return Detector(
            "detector:early_illness", "Coming down with something",
            illnessHave.Count >= 2,
            illnessHave.Count >= 2
                ? $"{illnessHave.Count} of 3 signals available; two sustained are needed to fire."
                : $"Only {illnessHave.Count} of 3 signals available. Two must be present before this can speak at all.",
            illnessParts.Where(m => !HasDerived(m)).Select(m => $"{Label(coverage, m)}: no scored readings").ToList(),
            EverSaid(FindingTypes.EarlyIllness), Evidence.GradeFor(Evidence.IllnessDetection)?.ToString());

        // Regime change and drift both read the slow metrics directly.
        var slowEnough = FindingRun.SlowMetrics
            .Where(m => coverage.Any(c => c.Metric == m && c.Readings >= HealthThresholds.DriftMinDays))
            .ToList();

        yield return Detector(
            "detector:regime_change", "A step to a new level that then held",
            slowEnough.Count > 0,
            slowEnough.Count > 0
                ? $"Watching {slowEnough.Count} of {FindingRun.SlowMetrics.Length} slow movers."
                : "No slow-moving metric has enough readings to detect a step in.",
            FindingRun.SlowMetrics.Except(slowEnough)
                .Select(m => $"{Label(coverage, m)}: fewer than {HealthThresholds.DriftMinDays} readings").ToList(),
            EverSaid(FindingTypes.RegimeChange), null);

        yield return Detector(
            "detector:drift", "Slow movement, invisible day to day",
            slowEnough.Count > 0,
            slowEnough.Count > 0
                ? $"Watching {slowEnough.Count} of {FindingRun.SlowMetrics.Length} slow movers over 90 days."
                : "No slow-moving metric has enough readings to fit a trend to.",
            FindingRun.SlowMetrics.Except(slowEnough)
                .Select(m => $"{Label(coverage, m)}: fewer than {HealthThresholds.DriftMinDays} readings").ToList(),
            EverSaid(FindingTypes.Drift), null);

        // Strain
        var hasAcwr = input.Derived.Any(d => d.Metric == MetricCatalogue.AcwrActiveCalories);
        var hasDebt = input.Derived.Any(d => d.Metric == MetricCatalogue.SleepDebtMinutes);
        yield return Detector(
            "detector:strain_risk", "Training load and sleep debt",
            hasAcwr || hasDebt,
            (hasAcwr, hasDebt) switch
            {
                (true, true) => "Both training load and sleep debt are being computed.",
                (true, false) => "Training load only; sleep debt has nothing to compute from.",
                (false, true) => "Sleep debt only; training load has nothing to compute from.",
                _ => "Neither derived value exists, so there is nothing to compare against a band.",
            },
            new[]
            {
                hasAcwr ? null : "Active calories, daily, for a training-load ratio",
                hasDebt ? null : "Sleep duration, daily, for a debt against your own need",
            }.Where(n => n is not null).Select(n => n!).ToList(),
            EverSaid(FindingTypes.StrainRisk), Evidence.GradeFor(MetricCatalogue.AcwrActiveCalories)?.ToString());

        // Staleness always runs; it is the one detector that needs no data to work.
        var expectedMissing = FindingRun.DailyExpected
            .Where(m => coverage.Any(c => c.Metric == m && c.State != "current"))
            .Select(m => Label(coverage, m))
            .ToList();
        yield return Detector(
            "detector:staleness", "Data that stopped arriving",
            true,
            expectedMissing.Count == 0
                ? $"All {FindingRun.DailyExpected.Length} daily metrics are arriving."
                : $"{expectedMissing.Count} expected daily metric(s) are not current: {string.Join(", ", expectedMissing)}.",
            [], EverSaid(FindingTypes.Staleness), null);

        // Labs
        var draws = input.Observations.Where(o => o.LabPanelId is not null)
            .Select(o => o.LabPanelId!.Value).Distinct().Count();
        yield return Detector(
            "detector:lab_anchor", "A blood result outside its range, or moved",
            draws > 0,
            draws switch
            {
                0 => "No blood work has been entered, so there is nothing to read against a range.",
                1 => "One draw. It can be read against its reference ranges, but nothing can be called a move until there is a second.",
                _ => $"{draws} draws, so results can be read against both their range and the previous draw.",
            },
            draws == 0 ? ["A blood panel, entered as a draw"] : draws == 1 ? ["A second draw, to see a direction"] : [],
            EverSaid(FindingTypes.LabAnchor), Evidence.GradeFor(Evidence.LabReferenceRange)?.ToString());
    }

    private static Capability Detector(
        string key, string label, bool canRun, string says, IReadOnlyList<string> needs, int everSaid, string? grade) =>
        new(key, "Detectors", label,
            !canRun ? Blind : everSaid > 0 ? Speaking : Quiet,
            says, needs, everSaid, grade);

    // ── Patterns ────────────────────────────────────────────────────────────────

    private static IEnumerable<Capability> PatternRows(Inputs input)
    {
        var patterns = Patterns.Run(input.Observations, input.AsOf, input.Ranges, input.Sex, input.Age);
        var grade = Evidence.GradeFor(Evidence.CompositePattern)?.ToString();

        foreach (var p in patterns)
        {
            // Counted is how many components were actually measurable. Zero means the
            // pattern never had a chance, which is a statement about the data rather
            // than about the body.
            var state = p.Fires ? Speaking : p.Counted == 0 ? Blind : Quiet;

            yield return new Capability(
                $"pattern:{p.Key}", "Patterns", p.Title, state,
                p.Fires ? p.Statement : p.HeldBecause ?? p.Statement,
                p.Components
                    .Where(c => c.Movement == Movement.Unmeasured)
                    .Select(c => $"{c.Label}: {c.Detail}")
                    .ToList(),
                0, grade);
        }
    }

    // ── Relationships ───────────────────────────────────────────────────────────

    private static Capability Relationships(Inputs input)
    {
        var found = Correlations.Run(input.Observations, input.AsOf);

        // How many pairs could even be tested. A correlation engine reporting nothing
        // because no pair cleared thirty overlapping days is not the same as one
        // reporting that nothing is related, and only this number separates them.
        var series = input.Observations
            .Where(o => o.ObservedDateLocal > input.AsOf.AddDays(-90))
            .GroupBy(o => o.Metric)
            .ToDictionary(g => g.Key, g => g.Select(o => o.ObservedDateLocal).Distinct().ToHashSet());

        var testable = 0;
        var total = 0;

        foreach (var driver in Correlations.Drivers)
            foreach (var outcome in Correlations.Outcomes)
            {
                if (driver == outcome) continue;
                total++;
                if (!series.TryGetValue(driver, out var d) || !series.TryGetValue(outcome, out var o)) continue;
                if (d.Intersect(o).Count() >= Correlations.MinPairedDays) testable++;
            }

        return new Capability(
            "relationships", "Relationships", "What your numbers move with",
            testable == 0 ? Blind : found.Count > 0 ? Speaking : Quiet,
            testable == 0
                ? $"No pair of metrics has {Correlations.MinPairedDays} overlapping days in the last 90, so nothing could be tested."
                : found.Count > 0
                    ? $"{found.Count} relationship(s) survived false-discovery control, out of {testable} testable pairs."
                    : $"{testable} of {total} pairs were testable and none survived. That is a real answer: nothing here is related strongly enough to act on.",
            testable == 0 ? ["Thirty days of overlap between a daily behaviour and a daily response"] : [],
            0, Evidence.GradeFor(Evidence.Correlation)?.ToString());
    }

    // ── Forecast ────────────────────────────────────────────────────────────────

    private static IEnumerable<Capability> Forecasts(Inputs input)
    {
        var grade = Evidence.GradeFor(Evidence.Forecast)?.ToString();

        foreach (var target in Enum.GetValues<Prediction.Target>())
        {
            var next = Prediction.Next(input.Days, target);
            var label = $"Tomorrow's {Prediction.Describe(target)}";

            if (next is null)
            {
                yield return new Capability(
                    $"forecast:{target}", "Forecast", label, Blind,
                    $"No reading to forecast from. At least {Prediction.MinTrainingDays} days of history are needed before a model is fitted at all.",
                    [$"{Prediction.MinTrainingDays} days of {Prediction.Describe(target)}"], 0, grade);
                continue;
            }

            // A model that lost to "assume no change" has run and has nothing better to
            // say than the dull answer. That is quiet, not blind, and definitely not
            // speaking -- reporting persistence as a prediction is the failure this
            // whole subsystem is built to avoid.
            var won = next.Method == "model";

            yield return new Capability(
                $"forecast:{target}", "Forecast", label,
                won ? Speaking : Quiet,
                won
                    ? $"The fitted model beats assuming no change by {next.Evidence.Skill:P0} on {next.Evidence.Persistence.Predictions} backtested days."
                    : $"The fitted model does not beat assuming no change, so today's value is returned instead and labelled as such. {next.Evidence.Verdict}",
                won ? [] : ["More history, or a target that is actually predictable from these features"],
                0, grade);
        }
    }

    // ── Interventions ───────────────────────────────────────────────────────────

    private static Capability InterventionRow(Inputs input)
    {
        var evaluations = InterventionEval.Run(input.Interventions, input.Observations, input.AsOf);
        var withVerdict = evaluations.Count(e =>
            e.Verdict is InterventionEval.Verdicts.Improved
                or InterventionEval.Verdicts.Worsened
                or InterventionEval.Verdicts.NoChange
                or InterventionEval.Verdicts.WouldHaveAnyway);

        var noTarget = evaluations.Count(e => e.Verdict == InterventionEval.Verdicts.NoTarget);
        var tooEarly = evaluations.Count(e => e.Verdict == InterventionEval.Verdicts.TooEarly);

        var needs = new List<string>();
        if (evaluations.Count == 0) needs.Add("Something recorded as started, with the metric it is meant to change");
        if (noTarget > 0) needs.Add($"{noTarget} recorded without a target metric, so they can never get a verdict");
        if (tooEarly > 0) needs.Add($"{tooEarly} still inside the first six weeks");

        return new Capability(
            "interventions", "Interventions", "Did what you changed actually work",
            evaluations.Count == 0 ? Blind : withVerdict > 0 ? Speaking : Quiet,
            evaluations.Count == 0
                ? "Nothing has been recorded as started, so there is nothing to check."
                : $"{evaluations.Count} recorded, {withVerdict} with a verdict.",
            needs, 0, Evidence.GradeFor(Evidence.InterventionEvaluation)?.ToString());
    }

    // ── Labs ────────────────────────────────────────────────────────────────────

    private static Capability LabRow(Inputs input, List<MetricCoverage> coverage)
    {
        var panels = input.Observations
            .Where(o => o.LabPanelId is not null)
            .GroupBy(o => o.LabPanelId!.Value)
            .ToList();

        var analytes = input.Observations.Where(o => o.LabPanelId is not null)
            .Select(o => o.Metric).Distinct().Count();

        var catalogued = coverage.Count(c => c.Group == MetricCatalogue.GroupLabs && !c.Metric.Equals(MetricKeys.NonHdl)
                                             && c.Metric != MetricKeys.HomaIr && c.Metric != MetricKeys.Egfr);

        var missing = coverage
            .Where(c => c.Group == MetricCatalogue.GroupLabs && c.Source == "lab" && c.Readings == 0)
            .Select(c => c.Label)
            .ToList();

        return new Capability(
            "labs", "Blood work", "Read against a printed range and the last draw",
            panels.Count == 0 ? Blind : panels.Count > 1 ? Speaking : Quiet,
            panels.Count switch
            {
                0 => $"No draws entered. {catalogued} analytes are understood and waiting.",
                1 => $"One draw covering {analytes} analytes. A position, not yet a direction.",
                _ => $"{panels.Count} draws covering {analytes} analytes, so movement between draws is readable.",
            },
            missing.Count > 12 ? ["A panel — most of the catalogue is empty"] : missing.Select(m => $"{m}: never drawn").ToList(),
            0, Evidence.GradeFor(Evidence.LabReferenceRange)?.ToString());
    }

    // ── The honest summary ──────────────────────────────────────────────────────

    private static List<string> Headline(Inputs input, List<MetricCoverage> coverage, List<Capability> capabilities)
    {
        var lines = new List<string>();

        var recorded = coverage.Count(c => c.Readings > 0);
        var oldest = coverage.Where(c => c.First is not null).Select(c => c.First!.Value).DefaultIfEmpty().Min();

        lines.Add(oldest == default
            ? "Nothing has ever been recorded."
            : $"{recorded} of {coverage.Count} metrics have ever been recorded, the oldest reading from {oldest:d MMM yyyy}.");

        var blind = capabilities.Count(c => c.State == Blind);
        if (blind > 0)
            lines.Add($"{blind} of {capabilities.Count} capabilities cannot run at all. That is a statement about missing data, not about your health — and it is the difference between a quiet system and a healthy one.");

        var quiet = capabilities.Count(c => c.State == Quiet);
        if (quiet > 0)
            lines.Add($"{quiet} ran with enough data to find something and found nothing. Those are the ones whose silence means something.");

        var stale = coverage.Where(c => c.State == "stale").Select(c => c.Label).ToList();
        if (stale.Count > 0)
            lines.Add($"Stopped arriving: {string.Join(", ", stale.Take(6))}{(stale.Count > 6 ? $" and {stale.Count - 6} more" : "")}.");

        // Deliberately last, and deliberately not a score.
        lines.Add("No overall figure is given. A single number summarising how well this is working would be exactly the kind of composite this system grades as experimental everywhere else.");

        return lines;
    }

    private static string Label(List<MetricCoverage> coverage, string metric) =>
        coverage.FirstOrDefault(c => c.Metric == metric)?.Label ?? metric;
}
