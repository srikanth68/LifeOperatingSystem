using Vitara.Insight.Health;

namespace Vitara.Tests;

// "What if I had slept an hour more?"
//
// The step from forecasting to something worth calling a clone, and the step where it
// is easiest to start lying. A linear model will answer any question put to it,
// including questions about a life this person has never lived, and it will answer
// them in the same confident tone as the ones it knows about. These tests pin the two
// refusals -- no model, and no precedent -- because the refusals are the feature.
public class WhatIfTests
{
    private static readonly DateOnly Start = new(2026, 1, 5);

    // A person whose readiness genuinely follows how much they slept, with a weekly
    // pattern on top so the model has something real to beat persistence with.
    private static List<Prediction.DayRow> Responsive(int days = 300)
    {
        var rng = new Random(3);
        var rows = new List<Prediction.DayRow>();
        var sleep = 430.0;

        for (var i = 0; i < days; i++)
        {
            var day = Start.AddDays(i);
            var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            // A wide enough spread that "an hour more" is a night this person has
            // actually had. A fixture whose sleep never varies would be refused by the
            // support check, which is correct behaviour and a useless test.
            sleep = 360 + (weekend ? 60 : 0) + rng.NextDouble() * 150;

            // Tomorrow's readiness follows tonight's sleep, plainly.
            var previousSleep = rows.Count > 0 ? rows[^1].SleepMinutes!.Value : sleep;

            rows.Add(new Prediction.DayRow(day,
                Readiness: 40 + previousSleep * 0.08 + rng.NextDouble() * 2,
                RestingHr: 55,
                Hrv: 45,
                SleepMinutes: sleep,
                ActiveCalories: 400 + rng.NextDouble() * 200));
        }

        // The last night is deliberately a short one: "what if I had slept an hour more"
        // is a question people ask the morning after a bad night, and it is the case
        // where the answer has to be inside what they have done before.
        rows[^1] = rows[^1] with { SleepMinutes = 400 };

        return rows;
    }

    private static (string, double)[] Hour => [("sleep", 60)];

    [Fact]
    public void Without_a_model_that_beat_the_dull_answer_there_is_no_lever()
    {
        // A random walk: nothing beats "tomorrow is like today", and a guess like that
        // has no opinion about sleep. Saying anything here would be inventing it.
        var rng = new Random(5);
        var value = 70.0;
        var rows = new List<Prediction.DayRow>();

        for (var i = 0; i < 260; i++)
        {
            value += (rng.NextDouble() - 0.5) * 6;
            rows.Add(new Prediction.DayRow(Start.AddDays(i), Readiness: value,
                Hrv: 45, SleepMinutes: 400 + rng.NextDouble() * 60, ActiveCalories: 500));
        }

        var scenario = Assert.Single(Prediction.WhatIf(rows, Prediction.Target.Readiness, Hour));

        Assert.False(scenario.Supported);
        Assert.Null(scenario.Change);
        Assert.Contains("no opinion about what you do differently", scenario.Answer);
    }

    [Fact]
    public void A_life_this_person_has_never_lived_is_refused_by_name()
    {
        // Four hours more than they have ever slept. The model would answer; the data
        // cannot, and the difference is the whole point.
        var scenarios = Prediction.WhatIf(Responsive(), Prediction.Target.Readiness, [("sleep", 240)]);
        var scenario = Assert.Single(scenarios);

        Assert.False(scenario.Supported);
        Assert.Contains("almost never done that", scenario.Answer);
        // And it says what they DO do, so the refusal is useful rather than a shrug.
        Assert.Contains("nine days in ten", scenario.Answer);
    }

    [Fact]
    public void A_plausible_hour_is_answered_in_the_units_of_the_thing_predicted()
    {
        var scenarios = Prediction.WhatIf(Responsive(), Prediction.Target.Readiness, Hour);
        var scenario = Assert.Single(scenarios);

        Assert.True(scenario.Supported, scenario.Answer);
        Assert.NotNull(scenario.Change);
        Assert.True(scenario.Change > 0, "this person's readiness follows their sleep");
        Assert.Contains("points", scenario.Answer);
    }

    [Fact]
    public void Every_answer_says_it_is_not_proof_of_cause()
    {
        var scenarios = Prediction.WhatIf(Responsive(), Prediction.Target.Readiness, Hour);

        Assert.All(scenarios.Where(x => x.Supported), x =>
            Assert.Contains("not proof that one caused the other", x.Answer));
    }

    [Fact]
    public void The_question_is_phrased_in_hours_when_it_is_hours()
    {
        var scenarios = Prediction.WhatIf(Responsive(), Prediction.Target.Readiness,
            [("sleep", 60), ("sleep", 30)]);

        Assert.Equal("What if I had 1 hour more sleep?", scenarios[0].Question);
        Assert.Contains("30 minutes more sleep", scenarios[1].Question);
    }

    [Fact]
    public void You_cannot_ask_what_more_sleep_does_to_your_sleep()
    {
        // The feature was dropped from that model as a duplicate of the target, so there
        // is nothing to move -- and saying "no change" without explaining why would look
        // like an answer.
        var scenarios = Prediction.WhatIf(Responsive(), Prediction.Target.SleepMinutes, Hour);
        var scenario = Assert.Single(scenarios);

        Assert.False(scenario.Supported);
        Assert.Contains("does not use sleep as an input", scenario.Answer);
    }

    [Fact]
    public void An_unknown_lever_is_not_silently_ignored()
    {
        var scenarios = Prediction.WhatIf(Responsive(), Prediction.Target.Readiness, [("moonphase", 1)]);
        var scenario = Assert.Single(scenarios);

        Assert.False(scenario.Supported);
    }

    [Fact]
    public void Nothing_at_all_produces_nothing_rather_than_an_exception()
    {
        Assert.Empty(Prediction.WhatIf([], Prediction.Target.Readiness, Hour));
    }

    // ── The other two targets, and the clamp ────────────────────────────────────

    [Fact]
    public void HRV_drops_its_duplicated_column()
    {
        Assert.DoesNotContain("HRV today", Prediction.FeatureNames(Prediction.Target.Hrv));
        Assert.Contains("HRV today", Prediction.FeatureNames(Prediction.Target.Readiness));
    }

    [Fact]
    public void Time_asleep_drops_its_duplicated_column()
    {
        Assert.DoesNotContain("sleep last night", Prediction.FeatureNames(Prediction.Target.SleepMinutes));
    }

    [Fact]
    public void A_forecast_is_never_reported_outside_what_the_scale_allows()
    {
        // A linear model extrapolates happily past the end of the world. Readiness of
        // 118 is not a bold forecast, it is a bug shown to someone as a number.
        var rows = Enumerable.Range(0, 200)
            .Select(i => new Prediction.DayRow(Start.AddDays(i),
                Readiness: i > 180 ? 99 : 60 + i * 0.2,
                Hrv: 45, SleepMinutes: 430, ActiveCalories: 500))
            .ToList();

        var forecast = Prediction.Next(rows, Prediction.Target.Readiness)!;

        Assert.InRange(forecast.Value, 0, 100);
        Assert.InRange(forecast.High, 0, 100);
        Assert.InRange(forecast.Low, 0, 100);
    }

    [Fact]
    public void Exported_weights_are_in_the_units_a_person_reads()
    {
        var rows = Responsive();
        var model = Prediction.Fit(rows, Prediction.Target.Readiness, rows.Count - 1)!;

        var weights = model.InOriginalUnits();

        Assert.Equal(Prediction.FeatureNames(Prediction.Target.Readiness).Length, weights.Count);
        Assert.All(weights, w => Assert.True(double.IsFinite(w)));
    }
}
