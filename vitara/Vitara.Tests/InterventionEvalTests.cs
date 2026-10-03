using Vitara.API.Controllers;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// Grading the user's own decisions, and mostly declining to.
//
// This is the only question in the system whose answer can embarrass it, and it is
// also the easiest feature here to get catastrophically wrong in a way nobody notices:
// a naive before-and-after comparison reports that EVERYTHING works. Not often, not
// usually — always. People start things at low points, low points are followed by
// ordinary ones, and the comparison credits the intervention every single time.
//
// A feature that always says yes is worse than no feature, because it launders noise
// into confidence. So most of what follows is about the cases where the honest answer
// is "cannot say", and the one case that matters most is a real improvement that is
// nonetheless indistinguishable from simply returning to normal.
public class InterventionEvalTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    private static Intervention Started(
        string name, int daysAgo, string? target = MetricKeys.TotalSleepMinutes, int? endedDaysAgo = null) => new()
    {
        Name = name,
        Kind = "protocol",
        StartedOnLocal = Today.AddDays(-daysAgo),
        EndedOnLocal = endedDaysAgo is { } d ? Today.AddDays(-d) : null,
        TargetMetric = target,
    };

    private static Observation Obs(string metric, double value, DateOnly day) => new()
    {
        Metric = metric,
        Value = value,
        ObservedDateLocal = day,
        ObservedAtLocal = day.ToDateTime(new TimeOnly(8, 0)),
        Tier = Tiers.Dense,
    };

    // A series built from a function of "days before today", with a little noise so a
    // quartile means something. Deterministic: a seeded generator, because a test that
    // fails one run in twenty is a test that gets deleted.
    private static List<Observation> Series(string metric, int days, Func<int, double> level, int seed = 11)
    {
        var rng = new Random(seed);
        return Enumerable.Range(0, days)
            .Select(i =>
            {
                var daysAgo = days - 1 - i;
                return Obs(metric, level(daysAgo) + (rng.NextDouble() - 0.5) * 20, Today.AddDays(-daysAgo));
            })
            .ToList();
    }

    private static Evaluation Eval(
        Intervention intervention, IEnumerable<Observation> observations,
        IEnumerable<Intervention>? others = null,
        IEnumerable<ExcludedPeriod>? excluded = null) =>
        InterventionEval.Run(
            (others ?? []).Concat([intervention]).ToList(),
            observations.ToList(), Today, excluded?.ToList(), null)
        .First(e => e.Id == intervention.Id);

    // ── Refusing before it even looks ───────────────────────────────────────────

    [Fact]
    public void WithoutADeclaredTargetThereIsNoVerdict()
    {
        // The guard against the other way of lying with this: look at thirty metrics
        // afterwards, find the one that improved, call it the effect. One of thirty
        // always improves. The target is declared when the intervention starts or it
        // is not declared at all.
        var e = Eval(Started("Magnesium", 200, target: null),
            Series(MetricKeys.TotalSleepMinutes, 400, _ => 420));

        Assert.Equal(InterventionEval.Verdicts.NoTarget, e.Verdict);
        Assert.Contains("choosing the answer after", e.Statement);
        Assert.Equal("none", e.Confidence);
    }

    [Fact]
    public void AMetricWithNoBetterDirectionCannotBeATarget()
    {
        // TSH is bad at both ends. "Did this improve it" has no answer, and inventing
        // one would mean picking a direction nobody picked.
        var e = Eval(Started("Iodine", 200, MetricKeys.Tsh), Series(MetricKeys.Tsh, 400, _ => 2.0));

        Assert.Equal(InterventionEval.Verdicts.NoDirection, e.Verdict);
    }

    [Fact]
    public void NothingIsConcludedInTheFirstSixWeeks()
    {
        // Two weeks of run-in because nothing acts instantly and the first fortnight
        // is mostly learning to do it, then four weeks to measure. The date is given
        // rather than just withheld, so there is something to wait for.
        var e = Eval(Started("Earlier bedtime", 20), Series(MetricKeys.TotalSleepMinutes, 400, _ => 420));

        Assert.Equal(InterventionEval.Verdicts.TooEarly, e.Verdict);
        Assert.Equal(Today.AddDays(-20 + 42), e.EarliestVerdict);
        Assert.Contains("2026", e.Statement);
    }

    [Fact]
    public void TwoThingsStartedTheSameMonthCannotBeToldApart()
    {
        // The most common way a self-experiment produces a confident wrong answer, and
        // one no amount of statistics fixes. Refused outright rather than reported
        // with a caveat nobody reads.
        var magnesium = Started("Magnesium", 120);
        var bedtime = Started("Earlier bedtime", 110);

        var e = Eval(magnesium, Series(MetricKeys.TotalSleepMinutes, 400, d => d > 120 ? 400 : 450), [bedtime]);

        Assert.Equal(InterventionEval.Verdicts.CannotSeparate, e.Verdict);
        Assert.Contains("Earlier bedtime", e.Statement);
        Assert.Contains("one thing at a time", e.Statement);
    }

    [Fact]
    public void ABeforeCannotBeReconstructedAfterTheFact()
    {
        // Somebody who starts measuring on the day they start the protocol has no
        // before, and no amount of after supplies one.
        var e = Eval(Started("Magnesium", 120),
            Series(MetricKeys.TotalSleepMinutes, 100, _ => 430));

        Assert.Equal(InterventionEval.Verdicts.NotEnoughData, e.Verdict);
        Assert.Contains("not reconstructable", e.Statement);
    }

    // ── The one that matters ────────────────────────────────────────────────────

    [Fact]
    public void AnImprovementOffABadPatchIsNotCreditedToTheIntervention()
    {
        // THE TEST THIS WHOLE FILE EXISTS FOR.
        //
        // Someone sleeps badly for a month, starts something, and sleeps normally
        // again. The before-and-after difference is large and entirely real, and none
        // of it is evidence: the same person has dipped and recovered several times
        // before without changing anything, which is what the history says and what a
        // naive comparison cannot see.
        var start = 120;
        var observations = new List<Observation>();
        var rng = new Random(5);

        for (var daysAgo = 500; daysAgo >= 0; daysAgo--)
        {
            // A long-run normal of 430, with four earlier month-long dips to 370 that
            // recovered on their own, and the same dip immediately before the start.
            var dip = (daysAgo is >= 430 and < 460)
                      || (daysAgo is >= 360 and < 390)
                      || (daysAgo is >= 280 and < 310)
                      || (daysAgo is >= 200 and < 230)
                      || (daysAgo >= start && daysAgo < start + 28);

            // After the run-in: recovered to 425, slightly LESS than the 430 this
            // person returns to unaided. The improvement is real, large, and smaller
            // than their own history says to expect for free.
            var level = daysAgo < start - InterventionEval.RunInDays ? 425 : dip ? 370 : 430;

            observations.Add(Obs(MetricKeys.TotalSleepMinutes,
                level + (rng.NextDouble() - 0.5) * 16, Today.AddDays(-daysAgo)));
        }

        var e = Eval(Started("Magnesium", start), observations);

        Assert.Equal(InterventionEval.Verdicts.WouldHaveAnyway, e.Verdict);
        Assert.NotNull(e.Rebound);
        Assert.True(e.Rebound!.ComparableWindows >= 3);
        Assert.Contains("below-par", e.Statement);
        Assert.Contains("returning to normal", e.Statement);

        // The improvement is real and is reported. It is the ATTRIBUTION that is
        // refused, which is a different and much more useful thing than denying the
        // change happened.
        Assert.True(e.Change > 0);
    }

    [Fact]
    public void AnImprovementBiggerThanTheReboundIsCredited()
    {
        // The same shape of history, and a recovery that overshoots the old normal by
        // far more than any previous dip ever did. This is what the feature is for:
        // it has to be able to say yes, or saying no means nothing.
        var start = 120;
        var observations = new List<Observation>();
        var rng = new Random(5);

        for (var daysAgo = 500; daysAgo >= 0; daysAgo--)
        {
            var dip = (daysAgo is >= 430 and < 460)
                      || (daysAgo is >= 360 and < 390)
                      || (daysAgo is >= 280 and < 310)
                      || (daysAgo is >= 200 and < 230)
                      || (daysAgo >= start && daysAgo < start + 28);

            var after = daysAgo < start - InterventionEval.RunInDays;
            var level = after ? 500 : dip ? 370 : 430;

            observations.Add(Obs(MetricKeys.TotalSleepMinutes, level + (rng.NextDouble() - 0.5) * 16,
                Today.AddDays(-daysAgo)));
        }

        var e = Eval(Started("Earlier bedtime", start), observations);

        Assert.Equal(InterventionEval.Verdicts.Improved, e.Verdict);
        Assert.Equal("moderate", e.Confidence);
        Assert.Contains("more than that happened", e.Statement);
    }

    [Fact]
    public void WithoutEnoughHistoryToEstimateAReboundTheVerdictSaysSo()
    {
        // No earlier dips to learn from. The improvement is reported and the
        // confidence is not, which is the honest pair.
        var start = 70;
        var observations = Enumerable.Range(0, 200)
            .Select(i =>
            {
                var daysAgo = 199 - i;
                return Obs(MetricKeys.TotalSleepMinutes,
                    daysAgo >= start ? 400 : 470, Today.AddDays(-daysAgo));
            })
            .ToList();

        var e = Eval(Started("Earlier bedtime", start), observations);

        Assert.Equal(InterventionEval.Verdicts.Improved, e.Verdict);
        Assert.Null(e.Rebound);
        Assert.Equal("low", e.Confidence);
        Assert.Contains("not enough history", e.Statement);
    }

    // ── The ordinary answers ────────────────────────────────────────────────────

    [Fact]
    public void AChangeInsideTheUsualSpreadIsNoChange()
    {
        var e = Eval(Started("Magnesium", 120),
            Series(MetricKeys.TotalSleepMinutes, 400, _ => 430));

        Assert.Equal(InterventionEval.Verdicts.NoChange, e.Verdict);
        Assert.Contains("ordinary spread", e.Statement);
    }

    [Fact]
    public void SomethingThatMadeItWorseIsSaidPlainly()
    {
        // The verdict a health app is least inclined to give and most needs to.
        var start = 120;
        var e = Eval(Started("Late training block", start),
            Series(MetricKeys.TotalSleepMinutes, 400,
                d => d >= start - InterventionEval.RunInDays ? 450 : 370));

        Assert.Equal(InterventionEval.Verdicts.Worsened, e.Verdict);
        Assert.Contains("wrong direction", e.Statement);
    }

    [Fact]
    public void AReturnToTheLongRunUsualIsFlaggedAsExactlyThat()
    {
        var start = 120;
        var observations = Series(MetricKeys.TotalSleepMinutes, 400,
            d => d >= start && d < start + 28 ? 360 : 430);

        var e = Eval(Started("Magnesium", start), observations);

        Assert.Contains(e.Caveats, c => c.Contains("relative to a bad patch"));
    }

    // ── Context that changes the reading ────────────────────────────────────────

    [Fact]
    public void IllnessInsideTheAfterWindowIsSaidBeforeTheNumbersAre()
    {
        var start = 120;
        var illness = new ExcludedPeriod
        {
            StartLocal = Today.AddDays(-start + 20),
            EndLocal = Today.AddDays(-start + 30),
            Reason = "illness",
        };

        var e = Eval(Started("Magnesium", start),
            Series(MetricKeys.TotalSleepMinutes, 400, _ => 430), excluded: [illness]);

        Assert.Contains(e.Caveats, c => c.Contains("illness"));
    }

    [Fact]
    public void AnInterventionThatStoppedIsMeasuredUpToWhenItStopped()
    {
        var e = Eval(Started("Magnesium", 200, endedDaysAgo: 60),
            Series(MetricKeys.TotalSleepMinutes, 400, _ => 430));

        Assert.Contains(e.Caveats, c => c.Contains("Stopped on"));
        Assert.NotNull(e.After);
        Assert.True(e.After!.To <= Today.AddDays(-60));
    }

    // ── Everything that was not being tested ────────────────────────────────────

    [Fact]
    public void OtherMetricsThatMovedAreReportedAsNotTheThingBeingTested()
    {
        var start = 120;
        var observations = Series(MetricKeys.TotalSleepMinutes, 400, _ => 430)
            .Concat(Series(MetricKeys.RestingHeartRate, 400,
                d => d >= start - InterventionEval.RunInDays ? 54 : 48, seed: 3)
                .Select(o => Obs(o.Metric, o.Value / 20 + 45, o.ObservedDateLocal)))
            .ToList();

        var e = Eval(Started("Magnesium", start), observations);

        Assert.Contains(e.AlsoChanged, c => c.Metric == MetricKeys.RestingHeartRate);
        Assert.DoesNotContain(e.AlsoChanged, c => c.Metric == e.TargetMetric);
    }

    // ── The windows cannot drift apart ──────────────────────────────────────────

    [Fact]
    public void TheApiAndTheEvaluatorAgreeAboutTheWindows()
    {
        // Vitara.API does not reference Vitara.Insight, so the two numbers it needs to
        // say "no verdict before this date" are duplicated. Pinned here, because a
        // silent disagreement would promise a verdict on a date no verdict arrives.
        Assert.Equal(InterventionEval.RunInDays, InterventionEvalWindow.RunInDays);
        Assert.Equal(InterventionEval.WindowDays, InterventionEvalWindow.WindowDays);
    }

    [Fact]
    public void EveryEvaluationSaysSomethingAPersonCanRead()
    {
        var all = InterventionEval.Run(
            [Started("A", 5), Started("B", 300, target: null), Started("C", 200, MetricKeys.Tsh)],
            Series(MetricKeys.TotalSleepMinutes, 400, _ => 430),
            Today);

        Assert.Equal(3, all.Count);
        Assert.All(all, e => Assert.False(string.IsNullOrWhiteSpace(e.Statement)));
        Assert.All(all, e => Assert.False(string.IsNullOrWhiteSpace(e.Verdict)));
    }
}
