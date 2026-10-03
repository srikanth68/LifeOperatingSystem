using System.Text.Json;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Insight.Health;

// Which way one measure has gone, over the window appropriate to how often it is taken.
public enum Movement
{
    // Never recorded, recorded once, or recorded too rarely for the window to say
    // anything. Deliberately distinct from Steady: "not moving" and "we cannot tell"
    // are opposite statements and collapsing them is how a gap becomes reassurance.
    Unmeasured,

    Steady,
    Favourable,
    Unfavourable,
}

// One measure's contribution to a pattern.
public record PatternComponent(
    string Metric,
    string Label,
    Movement Movement,
    string Detail,
    string Basis,          // how it was judged: trend window, or against the previous draw
    bool Counts);          // false when it is real but redundant -- see Derivations

// What several measures, read together, are doing.
public record PatternResult(
    string Key,
    string Title,
    string Statement,
    string Interpretation,
    IReadOnlyList<PatternComponent> Components,
    int Moving,
    int Counted,
    int Unmeasured,
    bool Fires,
    string? HeldBecause);

// Several measures moving the same way at once.
//
// Every other detector here asks a question about ONE metric: is this unusual, is this
// drifting, did this step to a new level. That is the right question for an acute
// signal and the wrong one for the thing the longevity literature is actually about,
// which is a slow correlated drift across a domain. Weight up four kilos is a bad
// year. Weight up, waist up, triglycerides up and fasting insulin up is a direction,
// and no single-metric detector can see it -- each component on its own is inside the
// range where nobody would mention it.
//
// FOUR THINGS MAKE THIS HONEST RATHER THAN A COUNTER, and without any of them it
// manufactures patterns:
//
//   1. CORRELATED MEASURES DO NOT VOTE TWICE. Non-HDL is total cholesterol minus HDL;
//      BMI is weight; HOMA-IR is glucose and insulin. Counting a derived value
//      alongside its own inputs turns one measurement into three votes, and three
//      votes is a pattern. They are still shown -- a reader wants to see them -- they
//      just do not count.
//
//   2. MISSING IS NOT STEADY, AND NOT EVIDENCE EITHER WAY. A pattern over four
//      measures where three were never taken is not "one of four moving". It is one of
//      one, and the honest output says so. This is also the only place in the system
//      that can tell somebody which test would make the answer trustworthy.
//
//   3. THE WINDOW FOLLOWS THE CADENCE. A ring metric gets ninety days of daily
//      readings and a significance test. A waist gets a year, because monthly tape
//      measurements over ninety days is four points and four points is not a trend. A
//      lab gets the previous draw and the width of its reference range, because two
//      readings a year can never be fitted.
//
//   4. THE GOOD ONE FIRES TOO. "Weight down, waist down, lean mass holding" is the
//      single most useful sentence this system can produce, and a detector that only
//      speaks when something is wrong teaches people to dread opening the app. It is
//      also the doc's own first example, and it needs lean mass to be MEASURED --
//      weight alone cannot tell which kind of loss it was.
//
// Pure. Observations and a date in, patterns out.
public static class Patterns
{
    // Dense metrics: three months of daily readings. Long enough for a slow drift to
    // separate from a bad fortnight, short enough to still be about now.
    public const int DenseWindowDays = 90;

    // Hand-entered metrics: a year. Weight is entered often enough for ninety days,
    // but waist is not, and one window for both would quietly exclude the measure that
    // matters most to this pattern.
    public const int MediumWindowDays = 365;

    // How much a trend has to actually move the number, as a share of the number
    // itself, before it counts as movement.
    //
    // Mann-Kendall tests MONOTONICITY, not size. A metric that creeps upward by two
    // tenths of a beat over ninety days, with almost no noise around it, is as
    // statistically certain a trend as one that climbs ten beats -- and reporting the
    // first one as a measure "moving the wrong way" is true and useless. Real data is
    // noisy enough to hide this most of the time, which is exactly what makes it a bad
    // thing to rely on.
    //
    // Two per cent is crude and deliberately low: this is a floor against the
    // trivially-certain, not a judgement about what matters clinically. On a resting
    // heart rate of 54 it asks for about one beat; on an 88cm waist, under two
    // centimetres a year.
    private const double MinRelativeChange = 0.02;

    // A derived value does not get a vote of its own while anything it is derived from
    // is also in the pattern and measured. It is still reported.
    private static readonly Dictionary<string, string[]> Derivations = new()
    {
        [MetricKeys.NonHdl] = [MetricKeys.TotalCholesterol, MetricKeys.Hdl],
        [MetricKeys.HomaIr] = [MetricKeys.Glucose, MetricKeys.FastingInsulin],
        [MetricKeys.Egfr] = [MetricKeys.Creatinine],
        [MetricCatalogue.Bmi] = [MetricKeys.WeightKg],
        [MetricCatalogue.WaistToHeight] = [MetricKeys.WaistCircumferenceCm],
    };

    // A precondition that must hold before a pattern is even evaluated.
    private record Gate(string Metric, Movement[] AnyOf, string Unless);

    private record Definition(
        string Key,
        string Title,
        string Interpretation,
        string[] Components,
        int MinMoving = 2,
        bool ReadsFavourable = false,
        Gate[]? Gates = null);

    private static readonly Definition[] Catalogue =
    [
        new(
            "metabolic_drift",
            "Metabolic measures moving together",
            "Several measures that track how the body handles energy are moving the same way at once. " +
            "Each one alone has other explanations; together they describe a direction. " +
            "This is a reason to take the panel to a doctor and is not a diagnosis of anything.",
            [
                MetricKeys.WeightKg,
                MetricKeys.WaistCircumferenceCm,
                MetricKeys.Triglycerides,
                MetricKeys.FastingInsulin,
                MetricKeys.Glucose,
                MetricKeys.Hba1c,
                MetricKeys.HomaIr,
                MetricKeys.Alt,
            ]),

        new(
            "lipid_drift",
            "The lipid panel moving together",
            "More than one of the particles that carry cholesterol has moved in the same direction " +
            "since the last draw. What the right numbers are for you depends on your overall " +
            "cardiovascular risk, which this system does not know.",
            [
                MetricKeys.Ldl,
                MetricKeys.ApoB,
                MetricKeys.TotalCholesterol,
                MetricKeys.NonHdl,
                MetricKeys.Hdl,
            ]),

        new(
            "autonomic_strain",
            "Recovery measures drifting, slowly",
            "Resting heart rate, heart rate variability and sleep have been moving the wrong way " +
            "together for months. This is a different thing from the overnight illness signal: that " +
            "one is about two days, and a trend this long cannot be produced by a cold. Training " +
            "load, chronic stress, alcohol, a change in fitness and sleep debt all look like this.",
            [
                MetricKeys.RestingHeartRate,
                MetricKeys.HrvRmssd,
                MetricKeys.BreathingRate,
                MetricKeys.SleepEfficiency,
                MetricKeys.TotalSleepMinutes,
            ]),

        new(
            "lean_mass_loss",
            "Weight is coming off, and some of it is lean",
            "Weight falling is usually the goal. Weight falling while lean mass falls with it is a " +
            "different outcome wearing the same number on the scale, and it is the one that costs " +
            "strength, metabolic rate and, later, independence. Protein intake and resistance " +
            "training are the usual levers; this is worth raising before the next few kilos.",
            [MetricKeys.LeanMassKg, MetricKeys.Vo2Max],
            MinMoving: 1,
            Gates: [new Gate(MetricKeys.WeightKg, [Movement.Favourable], "weight is not coming down")]),

        new(
            "favourable_body_change",
            "Weight is coming off the way you would want",
            "Weight down, the waist going with it, and lean mass holding. This is the composition " +
            "change worth having, and it is the one the scale alone cannot tell you about.",
            [MetricKeys.WaistCircumferenceCm, MetricKeys.BodyFatPct],
            MinMoving: 1,
            ReadsFavourable: true,
            Gates:
            [
                new Gate(MetricKeys.WeightKg, [Movement.Favourable], "weight is not coming down"),

                // Lean mass must be MEASURED and not falling. Unmeasured is not a pass:
                // the whole claim is that this was the good kind of loss, and without
                // lean mass nobody can say that.
                new Gate(MetricKeys.LeanMassKg, [Movement.Steady, Movement.Favourable],
                    "lean mass is either falling too, or is not being measured — and without it, " +
                    "the scale cannot tell a good loss from a bad one"),
            ]),
    ];

    public static List<PatternResult> Run(
        IReadOnlyList<Observation> observations,
        DateOnly asOf,
        IReadOnlyList<ReferenceRange>? ranges = null,
        string? sex = null,
        int? age = null)
    {
        var judged = new Dictionary<string, PatternComponent>();

        PatternComponent Judge(string metric)
        {
            if (judged.TryGetValue(metric, out var cached)) return cached;
            var c = Evaluate(metric, observations, asOf, ranges ?? [], sex, age);
            judged[metric] = c;
            return c;
        }

        var results = new List<PatternResult>();

        foreach (var def in Catalogue)
        {
            var gatesHeld = def.Gates?.FirstOrDefault(g => !g.AnyOf.Contains(Judge(g.Metric).Movement));

            var components = def.Components
                .Select(Judge)
                .Select(c => c with { Counts = CountsToward(c, def, judged, Judge) })
                .ToList();

            var wanted = def.ReadsFavourable ? Movement.Favourable : Movement.Unfavourable;

            var moving = components.Count(c => c.Counts && c.Movement == wanted);
            var counted = components.Count(c => c.Counts && c.Movement != Movement.Unmeasured);
            var unmeasured = components.Count(c => c.Movement == Movement.Unmeasured);

            // The favourable pattern has to be clean. One measure going the wrong way
            // is enough to stop calling it a good change, because the whole value of
            // the sentence is that it is unqualified.
            var contradicted = def.ReadsFavourable
                && components.Any(c => c.Counts && c.Movement == Movement.Unfavourable);

            // Half the measures that were actually taken, as well as the floor. Two of
            // eight is a coincidence; two of three is a direction. Without the
            // proportion, adding analytes to the pattern would make it quietly harder
            // to fire, which is backwards.
            var majority = counted > 0 && moving * 2 >= counted;

            var fires = gatesHeld is null && !contradicted && moving >= def.MinMoving && majority;

            var held =
                gatesHeld is not null ? gatesHeld.Unless
                : contradicted ? "one of the other measures is going the wrong way"
                : moving < def.MinMoving && counted == 0 ? "none of these are being measured often enough to say"
                : moving == 0 ? "none of these are moving that way"
                : moving < def.MinMoving ? $"only {Count(moving, "measure")} moving, and {def.MinMoving} is the floor"
                : !majority ? "fewer than half the measures taken are moving this way"
                : null;

            results.Add(new PatternResult(
                def.Key,
                def.Title,
                Statement(def, components, moving, counted, unmeasured, fires),
                def.Interpretation,
                components,
                moving,
                counted,
                unmeasured,
                fires,
                held));
        }

        return results;
    }

    // Whether this component's movement is its own evidence, or a restatement of
    // another component already counted.
    private static bool CountsToward(
        PatternComponent component,
        Definition def,
        Dictionary<string, PatternComponent> judged,
        Func<string, PatternComponent> judge)
    {
        if (!Derivations.TryGetValue(component.Metric, out var inputs)) return true;

        // Only suppressed when an input is both IN this pattern and measured. A HOMA-IR
        // in a pattern that does not also list glucose is standing on its own.
        return !inputs.Any(i => def.Components.Contains(i) && judge(i).Movement != Movement.Unmeasured);
    }

    // ── Judging one measure ─────────────────────────────────────────────────────

    private static PatternComponent Evaluate(
        string metric,
        IReadOnlyList<Observation> observations,
        DateOnly asOf,
        IReadOnlyList<ReferenceRange> ranges,
        string? sex,
        int? age)
    {
        var info = MetricCatalogue.Find(metric);
        var label = info?.Label ?? metric;
        var polarity = MetricDirection.Polarity(metric);

        // Read the way the data was actually collected, not the way the catalogue
        // expects it to be. Glucose is catalogued as the medium tier because a home
        // meter produces it often -- but somebody whose only glucose comes off two
        // blood draws a year has sparse data wearing a medium label, and running a
        // trend test on it returns "too few readings" when the right answer is "it
        // moved this much since the last draw".
        var fromDraws = observations.Any(o => o.Metric == metric && o.LabPanelId is not null);
        var denseEnough = observations.Count(o => o.Metric == metric && o.LabPanelId is null) >= 10;

        return info?.Tier == Tiers.Sparse || (fromDraws && !denseEnough)
            ? Sparse(metric, label, polarity, observations, ranges, sex, age)
            : Dense(metric, label, polarity, info?.Tier ?? Tiers.Dense, observations, asOf, info?.Decimals ?? 1);
    }

    // Dense and medium: a significance-tested trend over the window the cadence
    // supports. Mann-Kendall decides whether the slope means anything, which is the
    // same gate the drift detector uses -- a slope always exists, and fitting a line
    // to noise returns a line.
    private static PatternComponent Dense(
        string metric, string label, int polarity, string tier,
        IReadOnlyList<Observation> observations, DateOnly asOf, int decimals)
    {
        var window = tier == Tiers.Medium ? MediumWindowDays : DenseWindowDays;
        var months = window / 30;

        var points = observations
            .Where(o => o.Metric == metric && o.ObservedDateLocal > asOf.AddDays(-window))
            .GroupBy(o => o.ObservedDateLocal)
            .Select(g => (g.Key.DayNumber, Value: g.Average(o => o.Value)))
            .OrderBy(p => p.DayNumber)
            .ToList();

        var basis = $"{months}-month trend";

        if (Statistics.Trend(points) is not { } trend)
            return new PatternComponent(metric, label, Movement.Unmeasured,
                points.Count == 0
                    ? "never recorded"
                    : $"only {Count(points.Count, "reading")} in {months} months — too few to call a trend",
                basis, true);

        if (!trend.IsSignificant)
            return new PatternComponent(metric, label, Movement.Steady,
                $"no steady movement across {Count(points.Count, "reading")}", basis, true);

        var perMonth = trend.SlopePerDay * 30;
        var rising = trend.SlopePerDay > 0;

        // Certain, and trivial. Reported as steady rather than as a move, because it
        // is one: the slope is real and it has not taken the number anywhere.
        var span = points[^1].DayNumber - points[0].DayNumber;
        var across = Math.Abs(trend.SlopePerDay * span);
        var typical = Math.Abs(Statistics.Median(points.Select(pt => pt.Value).ToList()));

        if (typical > 0 && across < typical * MinRelativeChange)
            return new PatternComponent(metric, label, Movement.Steady,
                $"drifting {(rising ? "up" : "down")}, but only by {Round(across, decimals)} across {months} months", basis, true);

        var movement = polarity == MetricDirection.Neutral
            ? Movement.Steady
            : rising == (polarity == MetricDirection.HigherIsWorse) ? Movement.Unfavourable : Movement.Favourable;

        return new PatternComponent(metric, label, movement,
            $"{(rising ? "up" : "down")} about {Math.Abs(perMonth).ToString("0." + new string('#', decimals + 1))} a month",
            basis, true);
    }

    // Sparse: the last draw against the one before it, judged against the WIDTH of the
    // reference range rather than as a percentage -- the same rule the lab detector
    // uses, because two places disagreeing about what counts as a move is worse than
    // either rule being imperfect.
    private static PatternComponent Sparse(
        string metric, string label, int polarity,
        IReadOnlyList<Observation> observations,
        IReadOnlyList<ReferenceRange> ranges, string? sex, int? age)
    {
        const string basis = "against the previous draw";

        var draws = observations
            .Where(o => o.Metric == metric && o.LabPanelId is not null)
            .GroupBy(o => o.LabPanelId!.Value)
            .Select(g => g.OrderByDescending(o => o.ObservedDateLocal).First())
            .OrderBy(o => o.ObservedDateLocal)
            .ToList();

        if (draws.Count < 2)
            return new PatternComponent(metric, label, Movement.Unmeasured,
                draws.Count == 0 ? "never measured" : "measured once — a direction needs two draws",
                basis, true);

        var latest = draws[^1];
        var previous = draws[^2];
        var shift = latest.Value - previous.Value;

        var range = ReferenceRanges.For(ranges, metric, sex, age);
        var width = range is { Low: { } lo, High: { } hi } ? hi - lo : (double?)null;

        // Half the width of the range, OR a quarter of where it was -- whichever is
        // the smaller bar to clear.
        //
        // The lab detector uses the range width alone, deliberately: twenty per cent
        // of a TSH is noise and twenty per cent of an LDL is a different person's
        // cardiovascular risk, and the range width is the only scale that knows the
        // difference. That rule is right THERE, where one analyte is deciding whether
        // to raise a finding on its own and being conservative costs nothing.
        //
        // It is wrong here, and a test caught it: fasting insulin's printed range runs
        // 2.6 to 24.9, so half its width is 11 and a rise from 7 to 16 -- more than a
        // doubling, and the single clearest early sign of insulin resistance -- came
        // back as "steady". A range that wide cannot be the only scale.
        //
        // The two places differ because the jobs differ. A component here is only
        // asking whether it took part in a direction, and the pattern still needs two
        // of them plus a majority before it says anything, so a slightly lower bar is
        // bounded. A missed component is not: it silently removes the evidence that
        // the pattern exists at all.
        var bar = width is { } w and > 0
            ? Math.Min(w * 0.5, Math.Abs(previous.Value) * 0.25)
            : Math.Abs(previous.Value) * 0.25;

        var moved = Math.Abs(shift) >= bar;

        var detail = $"{Number(previous.Value)} to {Number(latest.Value)} since {previous.ObservedDateLocal:MMM yyyy}";

        if (!moved || polarity == MetricDirection.Neutral)
            return new PatternComponent(metric, label, Movement.Steady, detail, basis, true);

        var movement = (shift > 0) == (polarity == MetricDirection.HigherIsWorse)
            ? Movement.Unfavourable
            : Movement.Favourable;

        return new PatternComponent(metric, label, movement, detail, basis, true);
    }

    // ── Saying it ───────────────────────────────────────────────────────────────

    private static string Statement(
        Definition def, List<PatternComponent> components, int moving, int counted, int unmeasured, bool fires)
    {
        var names = components
            .Where(c => c.Counts && c.Movement == (def.ReadsFavourable ? Movement.Favourable : Movement.Unfavourable))
            .Select(c => c.Label)
            .ToList();

        var gap = unmeasured == 0
            ? ""
            : $" {Count(unmeasured, "of these measures is", "of these measures are")} not being measured often " +
              "enough to contribute, so this reads less of the picture than it could.";

        if (!fires)
            return counted == 0
                ? $"Nothing here can be judged yet.{gap}"
                : $"No pattern: {moving} of {Count(counted, "measure")} taken moving this way.{gap}";

        return names.Count switch
        {
            1 => $"{names[0]} is moving, and is the only one of these that is.{gap}",
            2 => $"{names[0]} and {names[1]} are both moving the same way.{gap}",
            _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]} are all moving the same way.{gap}",
        };
    }

    private static string Count(int n, string one, string? many = null) =>
        $"{n} {(n == 1 ? one : many ?? one + "s")}";

    private static string Number(double v) =>
        v == Math.Floor(v) ? ((long)v).ToString() : v.ToString("0.##");

    private static string Round(double v, int decimals) =>
        v.ToString("0." + new string('#', Math.Max(1, decimals + 1)));

    // ── As findings ─────────────────────────────────────────────────────────────

    // Only the ones that fire become findings. The rest are still served on the
    // patterns endpoint, where "no pattern, and here is what is missing" is the point
    // -- but a finding is a thing that opens a conversation, and "nothing is
    // happening" is not one.
    public static Finding ToFinding(PatternResult result, DateOnly asOf, bool favourable)
    {
        var direction = favourable ? "favourable" : "unfavourable";

        return new Finding
        {
            Key = $"{FindingTypes.Pattern}:{result.Key}:{direction}",
            Type = FindingTypes.Pattern,
            Metric = result.Key,
            Direction = direction,

            // Never high, for the same reason a lab result is never high: a composite
            // of slow drifts is a conversation to have, not an emergency, and this
            // combination is not a validated instrument in the first place.
            Severity = favourable ? "info" : "notable",

            // Each measured component is a vote; the unmeasured ones are the reason
            // this is not higher. Capped below certainty on purpose.
            Confidence = Math.Min(0.8, 0.4 + 0.1 * result.Moving - 0.05 * result.Unmeasured),

            Summary = $"{result.Title}. {result.Statement} {result.Interpretation}",
            EvidenceJson = JsonSerializer.Serialize(new
            {
                pattern = result.Key,
                result.Moving,
                result.Counted,
                result.Unmeasured,
                evidenceKey = Evidence.CompositePattern,
                evidenceGrade = Evidence.GradeFor(Evidence.CompositePattern)?.ToString(),
                components = result.Components.Select(c => new
                {
                    c.Metric,
                    movement = c.Movement.ToString().ToLowerInvariant(),
                    c.Detail,
                    c.Basis,
                    c.Counts,
                }),
            }),
            FirstDetectedLocal = asOf,
            LastDetectedLocal = asOf,
        };
    }
}
