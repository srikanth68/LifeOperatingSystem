namespace Vitara.Insight.Health;

// Tomorrow, predicted from this person's own history.
//
// The first piece of the bio signature: not a description of who someone is, but a
// claim about what happens next, which is the only kind of claim that can be wrong in
// public. Everything else in Vitara describes the past accurately; this is the part
// that can embarrass itself, and it is built so that it does so in the open.
//
// Three rules, all enforced here rather than written down and hoped for:
//
//   1. It is fitted on this person alone. No population model, no transfer from
//      anyone else's data. Forty days of one person is a small sample and is treated
//      as one.
//   2. It must beat the naive answers on this person's own history, measured by
//      rolling origin -- fit on the past, predict the next day, never on data the fit
//      has seen. "Tomorrow is like today" is a genuinely strong forecast for
//      physiology, and a model that cannot beat it is worse than useless, because it
//      is the same answer dressed up as insight.
//   3. When it loses, the naive answer is what ships, labelled as such. Losing is not
//      an error state and is not hidden.
public static class Prediction
{
    // One day of the person, as the forecast sees them. Nullable throughout: a night
    // the ring was not worn is a hole, and filling it with a plausible number would be
    // inventing the very thing being predicted.
    public record DayRow(
        DateOnly Day,
        double? Readiness = null,
        double? RestingHr = null,
        double? Hrv = null,
        double? SleepMinutes = null,
        double? ActiveCalories = null);

    // What can be predicted. All four are things a person asks about their tomorrow;
    // nothing here predicts a diagnosis, and nothing predicts anything about anyone else.
    public enum Target { Readiness, RestingHr, Hrv, SleepMinutes }

    public static string Describe(Target target) => target switch
    {
        Target.Readiness => "readiness",
        Target.RestingHr => "resting heart rate",
        Target.Hrv => "HRV",
        _ => "time asleep",
    };

    public static string UnitOf(Target target) => target switch
    {
        Target.Readiness => "points",
        Target.RestingHr => "bpm",
        Target.Hrv => "ms",
        _ => "minutes",
    };

    // The range a prediction is allowed to land in. A linear model extrapolates happily
    // past the end of the world; readiness of 118 is not a bold forecast, it is a bug
    // shown to someone as a number.
    private static (double Low, double High) Plausible(Target target) => target switch
    {
        Target.Readiness => (0, 100),
        Target.RestingHr => (30, 120),
        Target.Hrv => (5, 250),
        _ => (0, 900),
    };

    // Enough days that a fit is not describing a fortnight's weather. Below this the
    // model is not attempted at all.
    public const int MinTrainingDays = 60;

    // How much recent history the backtest scores over, at most.
    public const int MaxEvalDays = 180;

    // ── Features ────────────────────────────────────────────────────────────────
    //
    // Deliberately few and deliberately legible. Eight features on sixty rows is
    // already generous; the temptation with a year of wearable data is forty features
    // and a model that fits the noise beautifully. Each of these is something a person
    // would name if asked why they expect tomorrow to go badly.
    private static readonly string[] AllFeatures =
    [
        "today",              // the persistence anchor
        "week mean",          // where the level has been sitting
        "momentum",           // today against that level
        "sleep last night",
        "yesterday's effort",
        "HRV today",
        "weekend",            // tomorrow, not today
    ];

    // Predicting HRV from "HRV today" twice, or sleep from "sleep last night" twice, is
    // the same column entered under two names. Ridge tolerates it and the coefficients
    // then split arbitrarily between the pair, which makes the model unreadable for no
    // gain. The duplicate is dropped per target instead.
    private static int Duplicate(Target target) => target switch
    {
        Target.SleepMinutes => 3,
        Target.Hrv => 5,
        _ => -1,
    };

    public static string[] FeatureNames(Target target)
    {
        var skip = Duplicate(target);
        return AllFeatures.Where((_, i) => i != skip).ToArray();
    }

    // Which feature a "what if" can move. Levers only exist where the model kept the
    // column -- you cannot ask what an extra hour of sleep does to your sleep.
    public static int? LeverIndex(Target target, string lever)
    {
        var name = lever switch
        {
            "sleep" => "sleep last night",
            "effort" => "yesterday's effort",
            _ => null,
        };

        if (name is null) return null;

        var names = FeatureNames(target);
        var index = Array.IndexOf(names, name);
        return index < 0 ? null : index;
    }

    // Row i is the day the prediction is made FROM; `forDay` is the day being predicted.
    // Passed in rather than read from row i + 1, because the one prediction that matters
    // -- tomorrow -- has no row yet.
    private static double[]? Features(IReadOnlyList<DayRow> rows, int i, Target target, DateOnly forDay)
    {
        if (i < 7 || i >= rows.Count) return null;

        var today = Value(rows[i], target);
        if (today is null) return null;

        var week = rows.Skip(i - 6).Take(7).Select(r => Value(r, target)).Where(v => v is not null).Select(v => v!.Value).ToList();
        if (week.Count < 4) return null;

        var weekMean = week.Average();
        var sleep = rows[i].SleepMinutes;
        var effort = rows[i].ActiveCalories;
        var hrv = rows[i].Hrv;

        // A missing input is carried as the week's own level rather than as zero: zero
        // is a real and terrible value for every one of these, and a model trained with
        // zeros for missing nights learns that not wearing the ring predicts collapse.
        double[] all =
        [
            today.Value,
            weekMean,
            today.Value - weekMean,
            sleep ?? MeanOf(rows, i, r => r.SleepMinutes),
            effort ?? MeanOf(rows, i, r => r.ActiveCalories),
            hrv ?? MeanOf(rows, i, r => r.Hrv),
            forDay.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 1 : 0,
        ];

        var skip = Duplicate(target);
        return skip < 0 ? all : all.Where((_, index) => index != skip).ToArray();
    }

    private static double MeanOf(IReadOnlyList<DayRow> rows, int i, Func<DayRow, double?> pick)
    {
        var window = rows.Take(i + 1).Select(pick).Where(v => v is not null).Select(v => v!.Value).ToList();
        return window.Count == 0 ? 0 : window.Average();
    }

    private static double? Value(DayRow row, Target target) => target switch
    {
        Target.Readiness => row.Readiness,
        Target.RestingHr => row.RestingHr,
        Target.Hrv => row.Hrv,
        _ => row.SleepMinutes,
    };

    // ── The model ───────────────────────────────────────────────────────────────

    public record Model(double[] Coefficients, double Intercept, double[] Mean, double[] Scale, int TrainedOn)
    {
        public double Predict(double[] features)
        {
            var sum = Intercept;
            for (var i = 0; i < features.Length; i++)
                sum += Coefficients[i] * ((features[i] - Mean[i]) / Scale[i]);

            return sum;
        }

        // The weights, in the units a person uses, so an exported model can be read as
        // well as run: "each extra hour of sleep is worth about this much". Standardised
        // coefficients divided back through their own scale.
        public IReadOnlyList<double> InOriginalUnits() =>
            Coefficients.Select((c, i) => c / Scale[i]).ToList();
    }

    // Ridge rather than plain least squares. With sixty rows, seven correlated features
    // and a person whose sleep and effort move together, the unpenalised solution can
    // put a large positive weight on one and a large negative weight on its neighbour
    // -- an unstable fit that predicts well in sample and swings wildly out of it.
    private const double Lambda = 1.0;

    public static Model? Fit(IReadOnlyList<DayRow> rows, Target target, int through)
    {
        var x = new List<double[]>();
        var y = new List<double>();

        for (var i = 0; i < through && i + 1 < rows.Count; i++)
        {
            var f = Features(rows, i, target, rows[i + 1].Day);
            var next = Value(rows[i + 1], target);
            if (f is null || next is null) continue;

            x.Add(f);
            y.Add(next.Value);
        }

        if (x.Count < MinTrainingDays) return null;

        var n = x[0].Length;
        var mean = new double[n];
        var scale = new double[n];

        for (var j = 0; j < n; j++)
        {
            mean[j] = x.Average(row => row[j]);
            var variance = x.Average(row => Math.Pow(row[j] - mean[j], 2));
            // A feature that never moves would divide by zero and, standardised, carries
            // no information anyway; scale 1 leaves it at exactly zero for every row.
            scale[j] = variance > 1e-9 ? Math.Sqrt(variance) : 1;
        }

        var z = x.Select(row => row.Select((v, j) => (v - mean[j]) / scale[j]).ToArray()).ToList();
        var yMean = y.Average();

        // Normal equations on standardised, centred data, so the intercept drops out of
        // the system and comes back as the mean of y.
        var a = new double[n, n];
        var b = new double[n];

        for (var j = 0; j < n; j++)
        {
            for (var k = 0; k < n; k++)
                a[j, k] = z.Sum(row => row[j] * row[k]) + (j == k ? Lambda : 0);

            b[j] = z.Select((row, i) => row[j] * (y[i] - yMean)).Sum();
        }

        var beta = Solve(a, b);
        return beta is null ? null : new Model(beta, yMean, mean, scale, x.Count);
    }

    // Gaussian elimination with partial pivoting. Seven by seven; nothing here justifies
    // a linear algebra dependency.
    private static double[]? Solve(double[,] a, double[] b)
    {
        var n = b.Length;
        var m = new double[n, n + 1];

        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++) m[i, j] = a[i, j];
            m[i, n] = b[i];
        }

        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < n; row++)
                if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col])) pivot = row;

            if (Math.Abs(m[pivot, col]) < 1e-12) return null;   // singular: no model

            if (pivot != col)
                for (var j = col; j <= n; j++)
                    (m[col, j], m[pivot, j]) = (m[pivot, j], m[col, j]);

            for (var row = 0; row < n; row++)
            {
                if (row == col) continue;
                var factor = m[row, col] / m[col, col];
                for (var j = col; j <= n; j++) m[row, j] -= factor * m[col, j];
            }
        }

        var result = new double[n];
        for (var i = 0; i < n; i++) result[i] = m[i, n] / m[i, i];
        return result;
    }

    // ── The backtest ────────────────────────────────────────────────────────────

    public record Score(double Mae, double Rmse, int Predictions);

    public record Backtest(
        Score? Model,
        Score Persistence,
        Score Mean,
        double? Skill,          // 1 - model MAE / persistence MAE. Above zero is a win.
        bool ModelWins,
        string Verdict);

    // Rolling origin: fit on everything before the day, predict it, move on. Never on
    // data the fit has seen, because a model scored on its own training days will
    // always look excellent and will always disappoint the first morning it is used.
    public static Backtest Run(IReadOnlyList<DayRow> rows, Target target, int evalDays = MaxEvalDays)
    {
        var ordered = rows.OrderBy(r => r.Day).ToList();
        var modelErrors = new List<double>();
        var persistenceErrors = new List<double>();
        var meanErrors = new List<double>();

        var start = Math.Max(MinTrainingDays, ordered.Count - evalDays);

        for (var i = start; i + 1 < ordered.Count; i++)
        {
            var actual = Value(ordered[i + 1], target);
            var today = Value(ordered[i], target);
            if (actual is null || today is null) continue;

            var history = ordered.Take(i + 1).Select(r => Value(r, target)).Where(v => v is not null).Select(v => v!.Value).ToList();
            if (history.Count == 0) continue;

            persistenceErrors.Add(Math.Abs(actual.Value - today.Value));
            meanErrors.Add(Math.Abs(actual.Value - history.Average()));

            var model = Fit(ordered, target, i);
            var features = Features(ordered, i, target, ordered[i + 1].Day);
            if (model is not null && features is not null)
                modelErrors.Add(Math.Abs(actual.Value - model.Predict(features)));
        }

        if (persistenceErrors.Count == 0)
            return new Backtest(null, Empty, Empty, null, false,
                "Not enough history to test a forecast against anything yet.");

        var persistence = ScoreOf(persistenceErrors);
        var mean = ScoreOf(meanErrors);

        // Scored on the same days or not at all. A model evaluated on a different subset
        // of days from its rival is not being compared with it.
        var model_ = modelErrors.Count == persistenceErrors.Count ? ScoreOf(modelErrors) : null;

        var skill = model_ is null ? (double?)null : 1 - model_.Mae / persistence.Mae;

        // A hair's-breadth win is a tie. Requiring a real margin keeps the answer from
        // flipping between "model" and "today" as each morning's data arrives.
        var wins = skill is > 0.02 && model_ is not null && model_.Mae < mean.Mae;

        return new Backtest(model_, persistence, mean, skill, wins,
            Verdict(model_, persistence, mean, skill, wins, target));
    }

    private static readonly Score Empty = new(0, 0, 0);

    private static Score ScoreOf(IReadOnlyList<double> errors) => new(
        errors.Average(),
        Math.Sqrt(errors.Average(e => e * e)),
        errors.Count);

    private static string Verdict(Score? model, Score persistence, Score mean, double? skill, bool wins, Target target)
    {
        var what = Describe(target);
        var unit = UnitOf(target);

        if (model is null)
            return $"No forecast for {what} yet: there is not enough history to fit one and test it honestly. " +
                   $"Assuming tomorrow is like today is off by {persistence.Mae:0.0} {unit} on average.";

        var scored = $"Tested over {model.Predictions} days, one day at a time, never on data the fit had seen.";

        return wins
            ? $"{scored} The forecast is off by {model.Mae:0.0} {unit} on average, against {persistence.Mae:0.0} for " +
              $"assuming tomorrow is like today and {mean.Mae:0.0} for assuming an average day — " +
              $"{skill:P0} better than the closest of the two, so it is what gets used."
            : $"{scored} The forecast is off by {model.Mae:0.0} {unit} on average and assuming tomorrow is like " +
              $"today is off by {persistence.Mae:0.0}, so the forecast is not earning its keep. Tomorrow is " +
              "reported as today, said plainly, until it does.";
    }


    // ── What if ─────────────────────────────────────────────────────────────────
    //
    // The step from a forecast to something worth calling a clone: not "tomorrow will
    // be 74", but "on days that looked like today, when you had slept an hour more,
    // tomorrow was typically three points better".
    //
    // Two rules keep this from becoming a lie, and both refuse rather than guess.
    //
    //   1. NOT CAUSAL. The model was fitted on what this person happened to do, so a
    //      lever moves an association, not a cause. An hour more sleep on the days
    //      someone slept more may have come with a quiet evening, no alcohol and no
    //      late session, and the model is carrying all of that. Every scenario says so.
    //   2. NO EXTRAPOLATION. If the person has never slept nine hours, their data
    //      cannot answer what nine hours does. A scenario outside the middle of what
    //      they have actually done is refused by name rather than answered confidently.
    public record Scenario(
        string Lever,
        double Delta,
        string Question,
        double? From,
        double? To,
        double? Change,
        bool Supported,
        string Answer);

    // Where the lever's own history sits. Used to decide whether a scenario is inside
    // what this person has actually done.
    private static (double Low, double High)? LeverRange(
        IReadOnlyList<DayRow> rows, Target target, string lever)
    {
        var values = rows
            .Select(r => lever == "sleep" ? r.SleepMinutes : r.ActiveCalories)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .OrderBy(v => v)
            .ToList();

        if (values.Count < 30) return null;

        // The middle ninety per cent. The tails of a wearable series are mostly the days
        // the device was confused, and a counterfactual anchored on those is a
        // counterfactual about a sensor fault.
        return (Statistics.Percentile(values, 0.05), Statistics.Percentile(values, 0.95));
    }

    public static IReadOnlyList<Scenario> WhatIf(
        IReadOnlyList<DayRow> rows, Target target, IReadOnlyList<(string Lever, double Delta)> asks)
    {
        var ordered = rows.OrderBy(r => r.Day).ToList();
        var results = new List<Scenario>();
        if (ordered.Count == 0) return results;

        var evidence = Run(ordered, target, MaxEvalDays);
        var model = evidence.ModelWins ? Fit(ordered, target, ordered.Count - 1) : null;
        var tomorrow = ordered[^1].Day.AddDays(1);
        var baseFeatures = Features(ordered, ordered.Count - 1, target, tomorrow);

        foreach (var (lever, delta) in asks)
        {
            var question = Question(lever, delta, target);

            // The honest refusal, and the common one. Without a model that beat the dull
            // answer there is no lever to pull: "tomorrow is like today" has no opinion
            // about sleep.
            if (model is null || baseFeatures is null)
            {
                results.Add(new Scenario(lever, delta, question, null, null, null, false,
                    "Nothing can be said about this yet. Tomorrow is still best guessed as a copy of today, " +
                    "and a guess like that has no opinion about what you do differently."));
                continue;
            }

            var index = LeverIndex(target, lever);
            if (index is null)
            {
                results.Add(new Scenario(lever, delta, question, null, null, null, false,
                    $"This forecast does not use {lever} as an input, so moving it would change nothing."));
                continue;
            }

            var current = baseFeatures[index.Value];
            var proposed = current + delta;
            var range = LeverRange(ordered, target, lever);

            if (range is null || proposed < range.Value.Low || proposed > range.Value.High)
            {
                results.Add(new Scenario(lever, delta, question, null, null, null, false,
                    range is null
                        ? $"Not enough days with {lever} recorded to answer this from your own history."
                        : $"You have almost never done that. Your own {lever} sits between " +
                          $"{range.Value.Low:0} and {range.Value.High:0} on nine days in ten, and nothing " +
                          "outside that can be answered from your data rather than invented."));
                continue;
            }

            var moved = baseFeatures.ToArray();
            moved[index.Value] = proposed;

            var from = Clamp(model.Predict(baseFeatures), target);
            var to = Clamp(model.Predict(moved), target);
            var change = to - from;

            results.Add(new Scenario(lever, delta, question,
                Math.Round(from, 1), Math.Round(to, 1), Math.Round(change, 1), true, Answer(change, target)));
        }

        return results;
    }

    private static string Question(string lever, double delta, Target target)
    {
        var direction = delta >= 0 ? "more" : "less";
        var size = Math.Abs(delta);

        var what = lever switch
        {
            "sleep" when size >= 60 => $"{size / 60:0.#} {(Math.Abs(size - 60) < 0.5 ? "hour" : "hours")} {direction} sleep",
            "sleep" => $"{size:0} minutes {direction} sleep",
            "effort" => $"{size:0} kcal {direction} activity",
            _ => $"{size:0} {direction} {lever}",
        };

        return $"What if I had {what}?";
    }

    private static string Answer(double change, Target target)
    {
        var unit = UnitOf(target);
        var what = Describe(target);

        // Below a tenth of a unit the model is saying nothing, and dressing that up as a
        // small effect would be the most misleading thing on the page.
        if (Math.Abs(change) < 0.1)
            return $"Your own days show no meaningful difference in tomorrow's {what}.";

        var direction = change > 0 ? "higher" : "lower";

        return $"On days like today, that went with tomorrow's {what} being about " +
               $"{Math.Abs(change):0.#} {unit} {direction}. That is what your days did together, " +
               "not proof that one caused the other.";
    }

    // ── What actually gets shown ────────────────────────────────────────────────

    public record Forecast(
        DateOnly Day,
        double Value,
        double Low,
        double High,
        string Method,          // "model" | "today" | "none"
        string Basis,
        Backtest Evidence);

    // The prediction for tomorrow, by whichever method earned it.
    public static Forecast? Next(IReadOnlyList<DayRow> rows, Target target, int evalDays = MaxEvalDays)
    {
        var ordered = rows.OrderBy(r => r.Day).ToList();
        if (ordered.Count == 0) return null;

        var last = ordered[^1];
        var today = Value(last, target);
        if (today is null) return null;

        var evidence = Run(ordered, target, evalDays);
        var tomorrow = last.Day.AddDays(1);

        // The interval is the method's own recent error, not a confidence interval from
        // the fit. What a reader wants to know is how wrong this has actually been.
        var spread = evidence.ModelWins ? evidence.Model!.Rmse : evidence.Persistence.Rmse;

        if (evidence.ModelWins)
        {
            // Fit on everything, now that the honest test is done.
            var model = Fit(ordered, target, ordered.Count - 1);
            var features = Features(ordered, ordered.Count - 1, target, tomorrow);

            if (model is not null && features is not null)
            {
                var value = Clamp(model.Predict(features), target);
                return new Forecast(tomorrow, value, Clamp(value - spread, target), Clamp(value + spread, target), "model",
                    $"Fitted on your own {model.TrainedOn} days and tested against the alternatives.", evidence);
            }
        }

        return new Forecast(tomorrow, today.Value, Clamp(today.Value - spread, target), Clamp(today.Value + spread, target), "today",
            "Tomorrow is reported as today, which nothing here has beaten yet.", evidence);
    }

    private static double Clamp(double value, Target target)
    {
        var (low, high) = Plausible(target);
        return Math.Clamp(value, low, high);
    }
}
