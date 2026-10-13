using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Insight.Health;

public static class ScenarioModes
{
    // A calculation with Vitara's own rules on the person's own numbers. Exact.
    public const string Arithmetic = "arithmetic";
    // From what this person's days have done together before. An association.
    public const string Learned = "learned";
    // Never done before, or a medication: no prediction, only how to find out.
    public const string Experiment = "experiment";
}

public record ScenarioAnswer(string Mode, string Question, string Answer, double? From, double? To, string? Unit, bool Supported);

public record ScenarioResult(
    string Change,
    string Title,
    string Mode,                       // the strongest mode any answer used
    IReadOnlyList<ScenarioAnswer> Answers,
    ExperimentPlan? Experiment,
    string Caveat);

public record ExperimentPlan(
    IReadOnlyList<string> SuggestedTargets,
    string HowItIsJudged,
    int DaysUntilFirstVerdict,
    string? DoctorNote);

// "What if I...?", answered honestly (Vitara ADR 0024).
//
// The three example questions -- a late night, a new diet, a new medication -- are three
// different kinds of question, and a one-person dataset can only answer the first. So
// every answer is labelled with how it was reached:
//
//   arithmetic  the app's own rules on your own numbers: sleep owed, load ramp. Exact.
//   learned     what your own days did when you did this before: the existing what-if,
//               next day only, an association, refused outside your own range.
//   experiment  never done before, or a medication: no prediction at all. What to
//               watch, and when an answer becomes possible.
//
// A medication is always an experiment and never gets an expected effect (ADR 0023).
//
// Pure. Everything already loaded goes in.
public static class Scenarios
{
    public static readonly string[] Changes =
        ["short_sleep", "late_bedtime", "more_training", "alcohol", "new_diet", "new_supplement", "new_medication"];

    public record Inputs(
        IReadOnlyList<(DateOnly Day, double Minutes)> Nights,           // total sleep per night
        IReadOnlyList<(DateOnly Day, double Calories)> ActiveCalories,
        IReadOnlyList<(DateOnly Day, double Minutes)> Bedtimes,         // around midnight
        IReadOnlyList<Prediction.DayRow> DayRows,
        IReadOnlyList<MetricCorrelation> Correlations,
        DateOnly AsOf);

    public static ScenarioResult Answer(string change, double? amount, int days, Inputs input)
    {
        days = Math.Clamp(days, 1, 14);

        return change switch
        {
            "short_sleep" => ShortSleep(amount ?? 360, days, input),
            "late_bedtime" => LateBedtime(amount ?? 90, days, input),
            "more_training" => MoreTraining(amount ?? 400, days, input),
            "alcohol" => Experiment(change, "Drinking more, or less", input,
                [MetricKeys.HrvRmssd, MetricKeys.RestingHeartRate, MetricKeys.SleepScore],
                AlcoholFromHistory(input)),
            "new_diet" => Experiment(change, "A new way of eating", input,
                [MetricKeys.WeightKg, MetricKeys.WaistCircumferenceCm, MetricKeys.Ldl, MetricKeys.Hba1c],
                [new ScenarioAnswer(ScenarioModes.Experiment, "What would a new diet do?",
                    "Your data has no days eating this way, so it cannot say. Logging the last meal time and " +
                    "alcohol makes the timing side of a diet learnable; the rest needs the experiment below.",
                    null, null, null, false)]),
            "new_supplement" => Experiment(change, "Starting a supplement", input,
                [MetricKeys.HrvRmssd, MetricKeys.SleepScore, MetricKeys.VitaminD], []),
            "new_medication" => Experiment(change, "Starting a medication", input,
                [MetricKeys.RestingHeartRate, MetricKeys.SystolicBp, MetricKeys.HrvRmssd], [], medication: true),
            _ => throw new ArgumentException($"Unknown change \"{change}\".", nameof(change)),
        };
    }

    // ── Sleep ───────────────────────────────────────────────────────────────────

    private static ScenarioResult ShortSleep(double minutesPerNight, int nights, Inputs input)
    {
        var answers = new List<ScenarioAnswer>();
        var owed = SleepOwed(input.Nights, input.AsOf, minutesPerNight, nights);
        if (owed is { } o)
            answers.Add(new ScenarioAnswer(ScenarioModes.Arithmetic,
                $"Sleep owed after {Plural(nights, "night")} of {Hours(minutesPerNight)}",
                $"About {Hours(o.After)} owed, from {Hours(o.Before)} now, against your need of {Hours(o.Need)} ({o.NeedBasis}).",
                Math.Round(o.Before), Math.Round(o.After), "min", true));

        var usual = UsualSleep(input.Nights, input.AsOf);
        if (usual is { } u)
            answers.AddRange(Learned(input, "sleep", minutesPerNight - u,
                $"after one night of {Hours(minutesPerNight)} instead of your usual {Hours(u)}"));

        return Result("short_sleep", $"{Plural(nights, "night")} of {Hours(minutesPerNight)}", answers);
    }

    private static ScenarioResult LateBedtime(double minutesLater, int nights, Inputs input)
    {
        var answers = new List<ScenarioAnswer>();
        var usual = UsualSleep(input.Nights, input.AsOf);

        // A later bedtime with the alarm unchanged is a shorter night. Said as the
        // assumption it is.
        if (usual is { } u && SleepOwed(input.Nights, input.AsOf, u - minutesLater, nights) is { } o)
            answers.Add(new ScenarioAnswer(ScenarioModes.Arithmetic,
                $"Sleep owed if you go to bed {Minutes(minutesLater)} later for {Plural(nights, "night")} and wake at the usual time",
                $"About {Hours(o.After)} owed, from {Hours(o.Before)} now.",
                Math.Round(o.Before), Math.Round(o.After), "min", true));

        var recent = input.Bedtimes.Where(b => b.Day > input.AsOf.AddDays(-14)).Select(b => b.Minutes).ToList();
        if (recent.Count >= HabitTiming.RegularityMinNights)
        {
            var usualBed = HabitTiming.CircularMeanMinutes(recent);
            var shifted = recent.Skip(Math.Min(nights, recent.Count))
                .Concat(Enumerable.Repeat(usualBed + minutesLater, Math.Min(nights, 14))).ToList();
            var before = HabitTiming.CircularSdMinutes(recent);
            var after = HabitTiming.CircularSdMinutes(shifted);
            answers.Add(new ScenarioAnswer(ScenarioModes.Arithmetic,
                "Bedtime regularity over the fortnight",
                $"Your bedtime spread would go from about {before:0} to {after:0} minutes.",
                before, after, "min", true));
        }

        if (usual is not null)
            answers.AddRange(Learned(input, "sleep", -minutesLater,
                $"after one night {Minutes(minutesLater)} shorter, waking at the usual time"));

        return Result("late_bedtime", $"Bed {Minutes(minutesLater)} later for {Plural(nights, "night")}", answers);
    }

    // The 14-night deficit, with the last `nights` nights replaced by the hypothetical.
    public static (double Before, double After, double Need, string NeedBasis)? SleepOwed(
        IReadOnlyList<(DateOnly Day, double Minutes)> nights, DateOnly asOf, double minutesPerNight, int count)
    {
        if (nights.Count < 7) return null;

        var need = SleepDebt.EstimateNeed(nights.Select(n => n.Minutes).ToList());
        var recent = nights.Where(n => n.Day > asOf.AddDays(-14)).OrderBy(n => n.Day).ToList();
        var before = SleepDebt.Accumulate(recent, need.Minutes);

        var kept = recent.Skip(Math.Min(count, recent.Count)).ToList();
        var future = Enumerable.Range(1, count).Select(i => (asOf.AddDays(i), minutesPerNight));
        var after = SleepDebt.Accumulate(kept.Concat(future).ToList(), need.Minutes);

        return (before, after, need.Minutes, need.Basis);
    }

    private static double? UsualSleep(IReadOnlyList<(DateOnly Day, double Minutes)> nights, DateOnly asOf)
    {
        var recent = nights.Where(n => n.Day > asOf.AddDays(-60)).Select(n => n.Minutes).ToList();
        return recent.Count < 14 ? null : Statistics.Median(recent);
    }

    // ── Training ────────────────────────────────────────────────────────────────

    private static ScenarioResult MoreTraining(double extraPerDay, int days, Inputs input)
    {
        var answers = new List<ScenarioAnswer>();
        if (LoadRamp(input.ActiveCalories, input.AsOf, extraPerDay, Math.Min(days, 7)) is { } r)
            answers.Add(new ScenarioAnswer(ScenarioModes.Arithmetic,
                $"Load ramp after {Plural(Math.Min(days, 7), "day")} with {extraPerDay:0} extra active calories",
                $"Your 7-day load against your 28-day load would go from {r.Before:0.00} to {r.After:0.00}. " +
                (r.After > HealthThresholds.AcwrHigh
                    ? $"Above {HealthThresholds.AcwrHigh:0.0} means ramping up faster than you have adapted."
                    : "Inside the band where load is rising no faster than you have been adapting."),
                r.Before, r.After, "ratio", true));

        answers.AddRange(Learned(input, "effort", extraPerDay, $"after a day with {extraPerDay:0} more active calories"));
        return Result("more_training", $"{extraPerDay:0} more active calories a day", answers);
    }

    public static (double Before, double After)? LoadRamp(
        IReadOnlyList<(DateOnly Day, double Calories)> daily, DateOnly asOf, double extraPerDay, int days)
    {
        var chronic = daily.Where(d => d.Day > asOf.AddDays(-28)).OrderBy(d => d.Day).ToList();
        var acute = chronic.Where(d => d.Day > asOf.AddDays(-7)).ToList();
        if (Statistics.AcuteChronicRatio(acute.Select(a => a.Calories).ToList(), chronic.Select(c => c.Calories).ToList()) is not { } before)
            return null;

        var typical = Statistics.Median(chronic.Select(c => c.Calories).ToList());
        var future = Enumerable.Repeat(typical + extraPerDay, days).ToList();

        var newAcute = acute.Skip(days).Select(a => a.Calories).Concat(future).ToList();
        var newChronic = chronic.Skip(days).Select(c => c.Calories).Concat(future).ToList();
        var after = Statistics.AcuteChronicRatio(newAcute, newChronic, minAcute: 1, minChronic: 1) ?? before;

        return (Math.Round(before, 2), Math.Round(after, 2));
    }

    // ── Learned: the existing what-if, labelled ─────────────────────────────────

    private static IEnumerable<ScenarioAnswer> Learned(Inputs input, string lever, double delta, string when)
    {
        foreach (var target in new[] { Prediction.Target.Hrv, Prediction.Target.RestingHr, Prediction.Target.Readiness })
        {
            var s = Prediction.WhatIf(input.DayRows, target, [(lever, Math.Round(delta))]).FirstOrDefault();
            if (s is null) continue;
            yield return new ScenarioAnswer(ScenarioModes.Learned,
                $"Your {Prediction.Describe(target)} the next morning, {when}",
                s.Answer, s.From, s.To, Prediction.UnitOf(target), s.Supported);
        }
    }

    private static IReadOnlyList<ScenarioAnswer> AlcoholFromHistory(Inputs input) =>
        input.Correlations
            .Where(c => c.Driver == MetricKeys.AlcoholUnits)
            .Select(c => new ScenarioAnswer(ScenarioModes.Learned,
                $"What your logged drinks have gone with, {(c.LagDays == 0 ? "the same day" : "the next day")}",
                $"Over {c.N} days, more drinks went with {(c.Rho > 0 ? "higher" : "lower")} " +
                $"{Lower(MetricCatalogue.Find(c.Outcome)?.Label ?? c.Outcome)} (rho {c.Rho:0.00}). An association in " +
                "your own data, not a measured effect.",
                null, null, null, true))
            .ToList();

    // ── Experiment: no prediction, only how to find out ─────────────────────────

    private static ScenarioResult Experiment(
        string change, string title, Inputs input, string[] targets, IReadOnlyList<ScenarioAnswer> already,
        bool medication = false)
    {
        var daily = InterventionEval.RunInDays + InterventionEval.WindowDays;
        var plan = new ExperimentPlan(
            targets.Select(t => MetricCatalogue.Find(t)?.Label ?? t).ToList(),
            $"Pick one of these as the target before you start. Daily measures are judged on weeks three to six " +
            $"against the four weeks before, corrected for how much you would have bounced back anyway; a lab " +
            "is judged from a draw before and a draw after.",
            daily,
            medication
                ? "Start, stop or change a medication only with your doctor. Vitara never estimates what a medication will do; " +
                  "it can record the date and show what your data did afterwards."
                : null);

        var answers = already.Count > 0
            ? already
            : [new ScenarioAnswer(ScenarioModes.Experiment, $"What would {Lower(title)} do?",
                medication
                    ? "Vitara does not predict the effect of a medication. Record it when you start, so a change in your data is explained rather than flagged, and so the effect can be seen."
                    : "Your data has no stretch where you did this, so it cannot say. Run it as an experiment and it will tell you.",
                null, null, null, false)];

        return new ScenarioResult(change, title,
            answers.Any(a => a.Mode == ScenarioModes.Learned) ? ScenarioModes.Learned : ScenarioModes.Experiment,
            answers, plan,
            "An experiment answers the question; nothing here guesses at it in advance.");
    }

    private static ScenarioResult Result(string change, string title, List<ScenarioAnswer> answers) =>
        new(change, title,
            answers.Any(a => a.Mode == ScenarioModes.Arithmetic) ? ScenarioModes.Arithmetic
            : answers.Any(a => a.Mode == ScenarioModes.Learned) ? ScenarioModes.Learned
            : ScenarioModes.Experiment,
            answers.Count == 0
                ? [new ScenarioAnswer(ScenarioModes.Experiment, title,
                    "Not enough history to calculate this yet; a few weeks of nights and activity are needed.", null, null, null, false)]
                : answers,
            null,
            "Arithmetic answers apply Vitara's own rules to your numbers and are exact about those rules. Learned " +
            "answers come from what your own days did together: an association, next day only, never outside what you have done.");

    private static string Hours(double minutes) => $"{Math.Floor(minutes / 60):0} h {Math.Abs(minutes % 60):00}";
    private static string Minutes(double m) => m >= 60 ? $"{m / 60:0.#} h" : $"{m:0} min";
    private static string Plural(int n, string w) => $"{n} {w}{(n == 1 ? "" : "s")}";
    private static string Lower(string s) =>
        s.Length > 1 && char.IsUpper(s[0]) && !char.IsUpper(s[1]) ? char.ToLowerInvariant(s[0]) + s[1..] : s;
}
