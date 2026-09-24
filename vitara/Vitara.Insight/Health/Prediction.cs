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

    public enum Target { Readiness, RestingHr }

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
    public static readonly string[] FeatureNames =
    [
        "today",              // the persistence anchor
        "week mean",          // where the level has been sitting
        "momentum",           // today against that level
        "sleep last night",
        "yesterday's effort",
        "HRV today",
        "weekend",            // tomorrow, not today
    ];

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
        return
        [
            today.Value,
            weekMean,
            today.Value - weekMean,
            sleep ?? MeanOf(rows, i, r => r.SleepMinutes),
            effort ?? MeanOf(rows, i, r => r.ActiveCalories),
            hrv ?? MeanOf(rows, i, r => r.Hrv),
            forDay.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 1 : 0,
        ];
    }

    private static double MeanOf(IReadOnlyList<DayRow> rows, int i, Func<DayRow, double?> pick)
    {
        var window = rows.Take(i + 1).Select(pick).Where(v => v is not null).Select(v => v!.Value).ToList();
        return window.Count == 0 ? 0 : window.Average();
    }

    private static double? Value(DayRow row, Target target) =>
        target == Target.Readiness ? row.Readiness : row.RestingHr;

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
        var what = target == Target.Readiness ? "readiness" : "resting heart rate";
        var unit = target == Target.Readiness ? "points" : "bpm";

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
                var value = model.Predict(features);
                return new Forecast(tomorrow, value, value - spread, value + spread, "model",
                    $"Fitted on your own {model.TrainedOn} days and tested against the alternatives.", evidence);
            }
        }

        return new Forecast(tomorrow, today.Value, today.Value - spread, today.Value + spread, "today",
            "Tomorrow is reported as today, which nothing here has beaten yet.", evidence);
    }
}
