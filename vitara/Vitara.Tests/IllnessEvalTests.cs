using Vitara.Insight.Health;
using Vitara.Domain.Entities;

namespace Vitara.Tests;

// Checking the checker.
//
// The early-illness detector is the one place in this system with ground truth, and
// these tests pin what "it worked" is allowed to mean: a signal that arrives the same
// day the user marked themselves ill is confirmation, not warning, and must never be
// counted as the latter. A run of signal with no illness near it is a false alarm even
// if the user was feeling rough and did not mark it, because that is the reading that
// keeps the number honest.
public class IllnessEvalTests
{
    private static readonly DateOnly Start = new(2026, 6, 1);
    private static readonly HealthThresholdSet T = new(1.5, -1.5, 1.5, 0.15, 0.50);

    private static DateOnly D(int offset) => Start.AddDays(offset);

    // A day the detector will read as a hit: resting HR up, HRV down, both clearly.
    private static DailyVitals Ill(int offset) => new(D(offset), 1.9, -1.8, 0.2, 0.05);

    private static DailyVitals Well(int offset) => new(D(offset), 0.1, 0.2, 0.0, 0.0);

    private static List<DailyVitals> Timeline(int days, params int[] signalDays) =>
        Enumerable.Range(0, days)
            .Select(i => signalDays.Contains(i) ? Ill(i) : Well(i))
            .ToList();

    private static ExcludedPeriod Illness(int from, int to, string? notes = null) => new()
    {
        StartLocal = D(from), EndLocal = D(to), Reason = "illness", Notes = notes,
    };

    [Fact]
    public void No_marked_illness_is_reported_as_nothing_to_check_against()
    {
        var result = IllnessEval.Evaluate(Timeline(30), [], T, 2);

        Assert.Equal(0, result.Episodes);
        Assert.Contains("nothing to check the detector against", result.Verdict);
    }

    [Fact]
    public void No_readings_at_all_says_so()
    {
        var result = IllnessEval.Evaluate([], [Illness(10, 14)], T, 2);

        Assert.Contains("Nothing to evaluate", result.Verdict);
    }

    [Fact]
    public void A_signal_running_into_an_illness_is_a_catch_with_lead()
    {
        // Signal on days 8 and 9; the user marked themselves ill from day 11.
        var result = IllnessEval.Evaluate(Timeline(30, 8, 9), [Illness(11, 16)], T, 2);

        var episode = Assert.Single(result.PerEpisode);
        Assert.True(episode.Caught);
        Assert.Equal(1, result.Caught);
        // The signal needs two days to speak, so it first fires on day 9 -- two days
        // before the illness was marked.
        Assert.Equal(2, episode.LeadDays);
        Assert.Equal(0, result.FalseAlarms);
    }

    [Fact]
    public void A_signal_that_arrives_with_the_illness_is_confirmation_not_warning()
    {
        // Fires on day 12, marked from day 11: the detector is behind the user.
        var result = IllnessEval.Evaluate(Timeline(30, 11, 12), [Illness(11, 16)], T, 2);

        var episode = Assert.Single(result.PerEpisode);
        Assert.True(episode.Caught);
        Assert.Equal(-1, episode.LeadDays);
        Assert.Contains("confirmation rather than warning", result.Verdict);
    }

    [Fact]
    public void An_illness_with_no_signal_anywhere_is_a_miss()
    {
        var result = IllnessEval.Evaluate(Timeline(30), [Illness(11, 16)], T, 2);

        Assert.Equal(1, result.Missed);
        Assert.Equal(0, result.Caught);
        Assert.Null(result.MedianLeadDays);
        Assert.Contains("None were warned about in advance", result.Verdict);
    }

    [Fact]
    public void A_signal_long_before_an_illness_is_not_credited_to_it()
    {
        // Two weeks earlier is not a warning about this illness; it is a coincidence,
        // and counting it would let any noisy detector look prescient.
        var result = IllnessEval.Evaluate(Timeline(40, 2, 3), [Illness(25, 29)], T, 2);

        Assert.Equal(1, result.Missed);
        Assert.Equal(1, result.FalseAlarms);
    }

    [Fact]
    public void A_signal_with_no_illness_near_it_is_a_false_alarm()
    {
        var result = IllnessEval.Evaluate(Timeline(40, 5, 6, 7), [Illness(25, 29)], T, 2);

        var alarm = Assert.Single(result.FalseAlarmRuns);
        Assert.Equal(D(6), alarm.Start);
        Assert.Equal(D(7), alarm.End);
        Assert.Contains("One signal ran with no illness near it", result.Verdict);
    }

    [Fact]
    public void A_signal_still_running_just_after_recovery_is_not_a_new_false_alarm()
    {
        // Illness is marked by hand and rarely to the day. A signal trailing the marked
        // end by a day or two is the same episode.
        var result = IllnessEval.Evaluate(Timeline(40, 20, 21, 22, 23), [Illness(20, 22)], T, 2);

        Assert.Equal(0, result.FalseAlarms);
    }

    [Fact]
    public void A_broken_signal_counts_as_two_runs_because_the_user_was_told_twice()
    {
        var result = IllnessEval.Evaluate(Timeline(40, 4, 5, 10, 11), [], T, 2);

        Assert.Equal(2, result.FalseAlarms);
    }

    [Fact]
    public void A_small_sample_says_so_rather_than_producing_a_percentage()
    {
        var result = IllnessEval.Evaluate(Timeline(30, 8, 9), [Illness(11, 16)], T, 2);

        Assert.Contains("too few to conclude anything", result.Verdict);
        Assert.Contains("anecdote", result.Verdict);
    }

    [Fact]
    public void Three_or_more_episodes_are_reported_without_the_caveat()
    {
        var timeline = Timeline(120, 8, 9, 40, 41, 80, 81);
        var periods = new[] { Illness(11, 15), Illness(43, 47), Illness(83, 87) };

        var result = IllnessEval.Evaluate(timeline, periods, T, 2);

        Assert.Equal(3, result.Episodes);
        Assert.Equal(3, result.Caught);
        Assert.DoesNotContain("anecdote", result.Verdict);
        Assert.Equal(2, result.MedianLeadDays);
    }

    [Fact]
    public void Only_illness_periods_are_treated_as_labels()
    {
        // Travel and device changes are excluded from baselines for entirely different
        // reasons; reading them as illness would invent episodes nobody was ill for.
        var periods = new[]
        {
            new ExcludedPeriod { StartLocal = D(11), EndLocal = D(16), Reason = "travel" },
            new ExcludedPeriod { StartLocal = D(20), EndLocal = D(22), Reason = "device_change" },
        };

        var result = IllnessEval.Evaluate(Timeline(30, 8, 9), periods, T, 2);

        Assert.Equal(0, result.Episodes);
    }
}
