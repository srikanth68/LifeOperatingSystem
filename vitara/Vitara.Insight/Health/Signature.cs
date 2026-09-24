using Vitara.Domain.Entities;

namespace Vitara.Insight.Health;

// The shape of one person, in the few numbers that are actually theirs.
//
// "Bio signature", "digital clone" and "prediction model" are the same object seen
// from three angles, and only the third one can be tested. This file is the first two:
// a compact, portable description of how this body behaves -- when it sleeps, which
// days go badly, what it responds to, how long it takes to come back. Prediction.cs is
// the third, and it is the part that has to earn its place.
//
// Everything here is derived from the person's own record and nothing is compared with
// a population. That is the whole claim: not "you are above average", but "this is how
// you run, and here is how confidently we can say it".
//
// Small by design. A signature that needs a year of everything is a signature nobody
// has; each part below reports its own confidence and says when it does not have
// enough to speak.
public static class Signature
{
    // ── When this person sleeps ─────────────────────────────────────────────────

    // Minutes from midnight, so 23:10 is -50 and 00:40 is 40. Bedtimes cluster around
    // midnight and averaging clock times across it gives the famous wrong answer: a
    // person who goes to bed at 23:00 and 01:00 does not sleep at noon.
    public record Chronotype(
        double BedMinutes,
        double WakeMinutes,
        double BedVariabilityMinutes,
        double SocialJetlagMinutes,
        int Nights,
        string Note);

    public static Chronotype? WhenTheySleep(IReadOnlyList<SleepSession> nights, int minNights = 21)
    {
        var usable = nights.Where(n => n.TotalSleepMinutes > 0).ToList();
        if (usable.Count < minNights) return null;

        var bed = usable.Select(n => AroundMidnight(n.BedtimeStart)).ToList();
        var wake = usable.Select(n => n.BedtimeEnd.TimeOfDay.TotalMinutes).ToList();

        var weekday = usable.Where(n => !IsFreeNight(n)).Select(n => AroundMidnight(n.BedtimeStart)).ToList();
        var weekend = usable.Where(IsFreeNight).Select(n => AroundMidnight(n.BedtimeStart)).ToList();

        // Social jetlag: how far the free-night bedtime drifts from the working one. The
        // single most actionable number here, and the one people are most surprised by.
        var jetlag = weekday.Count >= 5 && weekend.Count >= 3
            ? Statistics.Median(weekend) - Statistics.Median(weekday)
            : 0;

        var variability = Statistics.MedianAbsoluteDeviation(bed) * 1.4826;

        return new Chronotype(
            Statistics.Median(bed),
            Statistics.Median(wake),
            variability,
            jetlag,
            usable.Count,
            Describe(variability, jetlag));
    }

    private static string Describe(double variability, double jetlag)
    {
        var steady = variability switch
        {
            < 30 => "Your bedtime barely moves.",
            < 60 => "Your bedtime moves by about an hour either way.",
            _ => "Your bedtime moves by more than an hour, which is usually the largest single thing " +
                 "under your control here.",
        };

        var drift = Math.Abs(jetlag) < 30
            ? "Free nights look like working ones."
            : $"On free nights you go to bed about {Math.Abs(jetlag) / 60.0:0.#} hours " +
              (jetlag > 0 ? "later" : "earlier") + ", which the following Monday tends to pay for.";

        return $"{steady} {drift}";
    }

    // Friday and Saturday nights: the sleep belongs to the day you wake, so a "Saturday
    // night" is recorded against Sunday.
    private static bool IsFreeNight(SleepSession n) =>
        n.BedtimeStart.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;

    private static double AroundMidnight(DateTime t)
    {
        var minutes = t.TimeOfDay.TotalMinutes;
        return minutes > 12 * 60 ? minutes - 24 * 60 : minutes;
    }

    // ── Which days go badly ─────────────────────────────────────────────────────

    public record WeekShape(
        IReadOnlyDictionary<DayOfWeek, double> ByDay,
        DayOfWeek Best,
        DayOfWeek Worst,
        double Spread,
        int Weeks,
        string Note);

    public static WeekShape? TheirWeek(IReadOnlyList<Prediction.DayRow> rows, int minWeeks = 6)
    {
        var scored = rows.Where(r => r.Readiness is not null).ToList();
        if (scored.Count < minWeeks * 7) return null;

        var byDay = scored
            .GroupBy(r => r.Day.DayOfWeek)
            .Where(g => g.Count() >= 3)
            .ToDictionary(g => g.Key, g => g.Average(r => r.Readiness!.Value));

        if (byDay.Count < 7) return null;

        var best = byDay.OrderByDescending(kv => kv.Value).First();
        var worst = byDay.OrderBy(kv => kv.Value).First();
        var spread = best.Value - worst.Value;

        // Three points is roughly the noise on a readiness score. Below that, naming a
        // worst day is naming the arithmetic rather than the week.
        var note = spread < 3
            ? "Your days are much the same as each other; nothing here is worth planning around."
            : $"{worst.Key} is your weakest day and {best.Key} your strongest, " +
              $"by about {spread:0} points of readiness.";

        return new WeekShape(byDay, best.Key, worst.Key, spread, scored.Count / 7, note);
    }

    // ── How long they take to come back ─────────────────────────────────────────

    public record Recovery(double? Days, int Episodes, string Note);

    // After a genuinely hard day, how many days until readiness is back where it was.
    //
    // Measured against this person's own hard days -- the top fifth of their effort --
    // rather than an absolute load, because the same session is a hard day for one
    // person and a warm-up for another.
    public static Recovery HowTheyRecover(IReadOnlyList<Prediction.DayRow> rows, int minEpisodes = 4)
    {
        var ordered = rows.OrderBy(r => r.Day).ToList();
        var efforts = ordered.Where(r => r.ActiveCalories is not null).Select(r => r.ActiveCalories!.Value).ToList();
        var readiness = ordered.Where(r => r.Readiness is not null).Select(r => r.Readiness!.Value).ToList();

        if (efforts.Count < 30 || readiness.Count < 30)
            return new Recovery(null, 0, "Not enough days with both effort and readiness recorded yet.");

        var hard = Statistics.Percentile(efforts, 0.8);
        var usual = Statistics.Median(readiness);

        var lengths = new List<double>();

        for (var i = 0; i + 1 < ordered.Count; i++)
        {
            if (ordered[i].ActiveCalories is not { } effort || effort < hard) continue;

            // Only count it if the next day actually dipped. A hard day that costs
            // nothing is a real and common outcome, and averaging it in as "zero days"
            // would make everyone look like they recover overnight.
            if (ordered[i + 1].Readiness is not { } after || after >= usual) continue;

            for (var j = i + 1; j < ordered.Count && j <= i + 7; j++)
            {
                if (ordered[j].Readiness is not { } value || value < usual) continue;

                lengths.Add(j - i);
                break;
            }
        }

        if (lengths.Count < minEpisodes)
            return new Recovery(null, lengths.Count,
                $"Only {lengths.Count} hard days followed by a dip so far, which is too few to say how long " +
                "you take to come back.");

        var median = Statistics.Median(lengths);

        return new Recovery(median, lengths.Count,
            $"After a hard day that costs you something, you are usually back to your normal in " +
            $"{median:0.#} {(median <= 1 ? "day" : "days")}, over {lengths.Count} such days.");
    }

    // ── What they respond to ────────────────────────────────────────────────────

    public record Response(string Driver, string Outcome, int LagDays, double Rho, int N);

    // Straight from the correlation run, strongest first, with the same caveat attached
    // wherever it is shown. Kept in the signature because "what moves your sleep" is
    // the part of this that is genuinely personal -- everything else is a level.
    public static IReadOnlyList<Response> WhatMovesThem(IReadOnlyList<MetricCorrelation> correlations, int take = 5) =>
        correlations
            .OrderByDescending(c => Math.Abs(c.Rho))
            .Take(take)
            .Select(c => new Response(c.Driver, c.Outcome, c.LagDays, Math.Round(c.Rho, 2), c.N))
            .ToList();

    // ── How much of this is actually known ──────────────────────────────────────

    public record Confidence(int Settled, int Learning, int DaysOfHistory, string Note);

    public static Confidence HowSure(
        IReadOnlyList<Baseline> baselines, int daysOfHistory, int minDays = 60)
    {
        var settled = baselines.Count(b => b.IsValid);
        var learning = baselines.Count(b => !b.IsValid);

        var note = daysOfHistory < minDays
            ? $"Built from {daysOfHistory} days. Below about {minDays} this describes a season rather than a person."
            : $"Built from {daysOfHistory} days, with {settled} measurements settled enough to be compared against.";

        return new Confidence(settled, learning, daysOfHistory, note);
    }
}
