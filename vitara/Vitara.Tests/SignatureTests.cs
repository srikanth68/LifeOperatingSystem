using Vitara.Insight.Health;
using Vitara.Domain.Entities;

namespace Vitara.Tests;

// The portrait half of the bio signature: how this body runs, in the few numbers that
// are genuinely its own.
//
// Two things are being defended here. The first is arithmetic: bedtimes cluster around
// midnight, and a mean taken the obvious way puts a person who sleeps at 23:00 and
// 01:00 firmly at noon. The second is restraint -- each part has to refuse to speak
// when it does not have enough, because a signature that always produces a confident
// sentence is one that produces a confident sentence about nothing.
public class SignatureTests
{
    private static readonly DateOnly Start = new(2026, 1, 5);   // a Monday

    private static SleepSession Night(int i, int bedHour, int bedMinute, int wakeHour = 7, int totalMinutes = 430)
    {
        var day = Start.AddDays(i);
        var bedDay = bedHour >= 12 ? day.AddDays(-1) : day;

        return new SleepSession
        {
            Id = $"s{i}",
            Day = day,
            BedtimeStart = new DateTime(bedDay.Year, bedDay.Month, bedDay.Day, bedHour, bedMinute, 0),
            BedtimeEnd = new DateTime(day.Year, day.Month, day.Day, wakeHour, 0, 0),
            TotalSleepMinutes = totalMinutes,
        };
    }

    private static Prediction.DayRow Row(int i, double? readiness = 75, double? effort = 480) =>
        new(Start.AddDays(i), Readiness: readiness, ActiveCalories: effort);

    // ── Chronotype ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_fortnight_is_not_enough_to_call_someone_a_late_sleeper()
    {
        var nights = Enumerable.Range(0, 14).Select(i => Night(i, 23, 0)).ToList();

        Assert.Null(Signature.WhenTheySleep(nights));
    }

    [Fact]
    public void Bedtimes_either_side_of_midnight_average_to_midnight_not_to_noon()
    {
        // The classic circular-mean error. Half the nights at 23:00, half at 01:00: the
        // answer is midnight, and the naive arithmetic says 12:00.
        var nights = Enumerable.Range(0, 30)
            .Select(i => i % 2 == 0 ? Night(i, 23, 0) : Night(i, 1, 0))
            .ToList();

        var chronotype = Signature.WhenTheySleep(nights)!;

        Assert.InRange(chronotype.BedMinutes, -61, 61);
    }

    [Fact]
    public void A_steady_sleeper_is_told_their_bedtime_barely_moves()
    {
        var nights = Enumerable.Range(0, 40).Select(i => Night(i, 22, 50 + i % 5)).ToList();

        var chronotype = Signature.WhenTheySleep(nights)!;

        Assert.True(chronotype.BedVariabilityMinutes < 30);
        Assert.Contains("barely moves", chronotype.Note);
    }

    [Fact]
    public void Staying_up_on_free_nights_is_measured_as_social_jetlag()
    {
        var nights = Enumerable.Range(0, 60).Select(i =>
        {
            var day = Start.AddDays(i);
            // The sleep is recorded against the day you wake, so a Friday night lands
            // on Saturday's row.
            var free = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            return free ? Night(i, 1, 30) : Night(i, 22, 30);
        }).ToList();

        var chronotype = Signature.WhenTheySleep(nights)!;

        Assert.True(chronotype.SocialJetlagMinutes > 120, $"expected a real drift, got {chronotype.SocialJetlagMinutes}");
        Assert.Contains("later", chronotype.Note);
    }

    // ── The week ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_month_is_not_enough_to_name_a_worst_day()
    {
        var rows = Enumerable.Range(0, 28).Select(i => Row(i)).ToList();

        Assert.Null(Signature.TheirWeek(rows));
    }

    [Fact]
    public void A_flat_week_is_reported_as_nothing_worth_planning_around()
    {
        // Two points of spread is inside the noise on a readiness score; naming a worst
        // day here would be naming the arithmetic.
        var rows = Enumerable.Range(0, 120).Select(i => Row(i, 75 + (i % 7) * 0.2)).ToList();

        var week = Signature.TheirWeek(rows)!;

        Assert.True(week.Spread < 3);
        Assert.Contains("much the same", week.Note);
    }

    [Fact]
    public void A_real_weekly_pattern_names_the_best_and_worst_day()
    {
        var rows = Enumerable.Range(0, 140).Select(i =>
        {
            var day = Start.AddDays(i);
            var readiness = day.DayOfWeek switch
            {
                DayOfWeek.Monday => 62.0,
                DayOfWeek.Sunday => 85.0,
                _ => 75.0,
            };
            return Row(i, readiness);
        }).ToList();

        var week = Signature.TheirWeek(rows)!;

        Assert.Equal(DayOfWeek.Monday, week.Worst);
        Assert.Equal(DayOfWeek.Sunday, week.Best);
        Assert.Contains("Monday is your weakest day", week.Note);
    }

    // ── Recovery ────────────────────────────────────────────────────────────────

    [Fact]
    public void Too_few_hard_days_is_said_rather_than_averaged()
    {
        var rows = Enumerable.Range(0, 60).Select(i => Row(i, 75, 480)).ToList();

        var recovery = Signature.HowTheyRecover(rows);

        Assert.Null(recovery.Days);
        Assert.Contains("too few", recovery.Note);
    }

    [Fact]
    public void A_hard_day_that_costs_a_day_is_measured_as_one_day()
    {
        // Every seventh day is hard, the day after dips, the day after that is back.
        var rows = Enumerable.Range(0, 140).Select(i =>
        {
            var hard = i % 7 == 0;
            var dipping = i % 7 == 1;
            return Row(i, dipping ? 60 : 78, hard ? 1200 : 400);
        }).ToList();

        var recovery = Signature.HowTheyRecover(rows);

        Assert.Equal(2, recovery.Days);          // dips on day+1, back on day+2
        Assert.True(recovery.Episodes >= 4);
        Assert.Contains("back to your normal", recovery.Note);
    }

    [Fact]
    public void A_hard_day_that_costs_nothing_is_not_counted_as_instant_recovery()
    {
        // Otherwise a person who trains hard and sails through looks like someone who
        // recovers overnight, and the number becomes meaningless for everyone.
        var rows = Enumerable.Range(0, 140).Select(i => Row(i, 78, i % 7 == 0 ? 1200 : 400)).ToList();

        var recovery = Signature.HowTheyRecover(rows);

        Assert.Equal(0, recovery.Episodes);
        Assert.Null(recovery.Days);
    }

    // ── What moves them ─────────────────────────────────────────────────────────

    [Fact]
    public void Responses_come_back_strongest_first_regardless_of_sign()
    {
        var correlations = new List<MetricCorrelation>
        {
            new() { Driver = "steps", Outcome = "total_sleep", LagDays = 0, Rho = 0.31, N = 88 },
            new() { Driver = "alcohol", Outcome = "hrv_rmssd", LagDays = 1, Rho = -0.58, N = 46 },
            new() { Driver = "total_sleep", Outcome = "readiness_score", LagDays = 1, Rho = 0.44, N = 84 },
        };

        var responses = Signature.WhatMovesThem(correlations);

        Assert.Equal("alcohol", responses[0].Driver);
        Assert.Equal(-0.58, responses[0].Rho);
        Assert.Equal(3, responses.Count);
    }

    // ── Confidence ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_short_history_is_described_as_a_season_rather_than_a_person()
    {
        var confidence = Signature.HowSure([], 30);

        Assert.Contains("a season rather than a person", confidence.Note);
    }

    [Fact]
    public void A_long_history_counts_what_is_settled()
    {
        var baselines = new List<Baseline>
        {
            new() { Metric = "resting_hr", IsValid = true },
            new() { Metric = "hrv_rmssd", IsValid = true },
            new() { Metric = "systolic_bp", IsValid = false },
        };

        var confidence = Signature.HowSure(baselines, 300);

        Assert.Equal(2, confidence.Settled);
        Assert.Equal(1, confidence.Learning);
        Assert.Contains("2 measurements settled", confidence.Note);
    }
}
