using Vitara.Insight.Health;

namespace Vitara.Tests;

// The first part of Vitara that makes a claim about the future, and therefore the
// first part that can be wrong where someone can see it.
//
// These tests exist to stop the usual failure: a model that looks excellent because it
// was scored on the days it was fitted on, shipped, and then quietly worse every
// morning than assuming tomorrow is like today. Each case below builds a person whose
// answer is known in advance and checks that the machinery reaches it -- including the
// cases where the honest answer is "this model is not worth using".
public class PredictionTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);

    private static List<Prediction.DayRow> Series(int days, Func<int, double> readiness, int seed = 7)
    {
        var rng = new Random(seed);
        return Enumerable.Range(0, days).Select(i => new Prediction.DayRow(
            Start.AddDays(i),
            Readiness: readiness(i),
            RestingHr: 55 + Math.Sin(i / 9.0) * 2,
            Hrv: 45 + rng.NextDouble() * 6,
            SleepMinutes: 420 + rng.NextDouble() * 60,
            ActiveCalories: 450 + rng.NextDouble() * 200)).ToList();
    }

    // ── Not enough history ──────────────────────────────────────────────────────

    [Fact]
    public void A_short_history_gets_no_model_and_says_so()
    {
        var rows = Series(40, i => 70 + Math.Sin(i / 5.0) * 8);

        var backtest = Prediction.Run(rows, Prediction.Target.Readiness);

        Assert.Null(backtest.Model);
        Assert.False(backtest.ModelWins);
        Assert.Contains("Not enough history", backtest.Verdict);
    }

    [Fact]
    public void Nothing_at_all_is_not_an_exception()
    {
        Assert.Null(Prediction.Next([], Prediction.Target.Readiness));
        Assert.Contains("Not enough history", Prediction.Run([], Prediction.Target.Readiness).Verdict);
    }

    // ── The honest-loss case, which is the important one ────────────────────────

    [Fact]
    public void A_random_walk_defeats_the_model_and_the_naive_answer_ships()
    {
        // A pure random walk is unforecastable by construction: the best possible guess
        // for tomorrow IS today. Any model that claims to beat this is fitting noise,
        // and shipping it would mean shipping confident nonsense every morning.
        var rng = new Random(11);
        var value = 70.0;
        var rows = new List<Prediction.DayRow>();

        for (var i = 0; i < 260; i++)
        {
            value += (rng.NextDouble() - 0.5) * 6;
            rows.Add(new Prediction.DayRow(Start.AddDays(i), Readiness: value,
                Hrv: 45 + rng.NextDouble() * 6, SleepMinutes: 430, ActiveCalories: 500));
        }

        var forecast = Prediction.Next(rows, Prediction.Target.Readiness)!;

        Assert.Equal("today", forecast.Method);
        Assert.Contains("not earning its keep", forecast.Evidence.Verdict);
        // And it still answers -- refusing to say anything would be its own kind of lie.
        Assert.Equal(rows[^1].Readiness!.Value, forecast.Value, 6);
    }

    // ── The win case ────────────────────────────────────────────────────────────

    [Fact]
    public void A_person_with_a_real_weekly_rhythm_is_forecast_better_than_today()
    {
        // Someone whose weekends are reliably worse: "tomorrow is like today" is wrong
        // every Friday and every Sunday by construction, and a model that knows which
        // day tomorrow is should beat it.
        var rows = Enumerable.Range(0, 300).Select(i =>
        {
            var day = Start.AddDays(i);
            var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            return new Prediction.DayRow(day,
                Readiness: (weekend ? 55 : 78) + Math.Sin(i / 31.0) * 2,
                Hrv: 45, SleepMinutes: 430, ActiveCalories: 500);
        }).ToList();

        var backtest = Prediction.Run(rows, Prediction.Target.Readiness);

        Assert.NotNull(backtest.Model);
        Assert.True(backtest.ModelWins, backtest.Verdict);
        Assert.True(backtest.Skill > 0.02);
        Assert.Contains("so it is what gets used", backtest.Verdict);
    }

    [Fact]
    public void The_forecast_uses_the_model_when_the_model_won()
    {
        var rows = Enumerable.Range(0, 300).Select(i =>
        {
            var day = Start.AddDays(i);
            var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            return new Prediction.DayRow(day, Readiness: weekend ? 55 : 78,
                Hrv: 45, SleepMinutes: 430, ActiveCalories: 500);
        }).ToList();

        var forecast = Prediction.Next(rows, Prediction.Target.Readiness)!;

        Assert.Equal("model", forecast.Method);
        Assert.Equal(rows[^1].Day.AddDays(1), forecast.Day);
        Assert.True(forecast.Low < forecast.Value && forecast.Value < forecast.High);
    }

    // ── No leakage ──────────────────────────────────────────────────────────────

    [Fact]
    public void The_backtest_never_fits_on_the_day_it_is_scoring()
    {
        // The tell: a single wild day cannot be predicted by a model that has not seen
        // it. If the error on that day is small, the fit saw it -- which is exactly the
        // mistake that makes a model look excellent and behave badly.
        var rows = Series(200, i => i == 150 ? 20 : 75).ToList();

        var errors = new List<double>();
        for (var i = Prediction.MinTrainingDays; i + 1 < rows.Count; i++)
        {
            var model = Prediction.Fit(rows, Prediction.Target.Readiness, i);
            if (model is null) continue;
            if (rows[i + 1].Day != Start.AddDays(150)) continue;

            var predicted = Prediction.Run(rows.Take(i + 2).ToList(), Prediction.Target.Readiness, 5);
            errors.Add(predicted.Model?.Mae ?? 0);
        }

        Assert.All(errors, e => Assert.True(e > 5, "a shock the fit has not seen cannot be predicted"));
    }

    // ── Missing data ────────────────────────────────────────────────────────────

    [Fact]
    public void A_night_without_the_ring_is_not_treated_as_a_terrible_night()
    {
        // The failure this guards: missing sleep carried as zero teaches the model that
        // not wearing the ring predicts collapse, and then every unworn night produces
        // an alarming forecast.
        var rows = Series(200, i => 75 + Math.Sin(i / 7.0) * 5)
            .Select((r, i) => i % 11 == 0 ? r with { SleepMinutes = null, Hrv = null } : r)
            .ToList();

        var forecast = Prediction.Next(rows, Prediction.Target.Readiness)!;

        Assert.InRange(forecast.Value, 55, 95);
    }

    [Fact]
    public void A_target_with_no_reading_today_gives_no_forecast()
    {
        var rows = Series(200, i => 75).Select(r => r with { Readiness = null }).ToList();

        Assert.Null(Prediction.Next(rows, Prediction.Target.Readiness));
    }

    // ── Resting heart rate, the second target ───────────────────────────────────

    [Fact]
    public void Resting_heart_rate_is_forecast_in_its_own_units()
    {
        var rows = Series(220, i => 75);

        var forecast = Prediction.Next(rows, Prediction.Target.RestingHr)!;

        Assert.InRange(forecast.Value, 45, 70);
        Assert.Contains("bpm", forecast.Evidence.Verdict);
    }

    [Fact]
    public void The_interval_is_the_error_this_method_actually_makes()
    {
        var rows = Series(220, i => 75 + Math.Sin(i / 6.0) * 6);

        var forecast = Prediction.Next(rows, Prediction.Target.Readiness)!;
        var spread = (forecast.High - forecast.Low) / 2;

        var evidence = forecast.Evidence;
        var expected = evidence.ModelWins ? evidence.Model!.Rmse : evidence.Persistence.Rmse;

        Assert.Equal(expected, spread, 6);
    }
}
