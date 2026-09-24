namespace Vitara.Domain.Health;

// Which way is bad, per metric.
//
// Most of the system is deliberately direction-blind: a baseline does not care whether
// high is good, and "unusual for you" reads the same either way. One decision needs to
// know, though, and it is the sharp one -- whether a sustained step to a new level is
// adopted as the new normal.
//
// A step down in resting heart rate after starting a medication is a new normal. The
// same step UP, with nothing recorded to explain it, is the thing the system exists to
// notice; adopting it would rebuild the baseline around the worse number and go quiet
// about exactly what it was watching for.
public static class MetricDirection
{
    public const int HigherIsBetter = 1;
    public const int HigherIsWorse = -1;
    public const int Neutral = 0;

    private static readonly Dictionary<string, int> Polarities = new()
    {
        [MetricKeys.RestingHeartRate]    = HigherIsWorse,
        [MetricKeys.BreathingRate]       = HigherIsWorse,
        [MetricKeys.SkinTempDeviation]   = HigherIsWorse,
        [MetricKeys.StressHighSeconds]   = HigherIsWorse,
        [MetricKeys.SystolicBp]          = HigherIsWorse,
        [MetricKeys.DiastolicBp]         = HigherIsWorse,
        [MetricKeys.Pulse]               = HigherIsWorse,
        [MetricKeys.Glucose]             = HigherIsWorse,
        [MetricKeys.WeightKg]            = HigherIsWorse,
        [MetricKeys.WaistCircumferenceCm]= HigherIsWorse,
        [MetricKeys.CardiovascularAge]   = HigherIsWorse,
        [MetricKeys.Ldl]                 = HigherIsWorse,
        [MetricKeys.Triglycerides]       = HigherIsWorse,
        [MetricKeys.TotalCholesterol]    = HigherIsWorse,
        [MetricKeys.Crp]                 = HigherIsWorse,

        [MetricKeys.HrvRmssd]            = HigherIsBetter,
        [MetricKeys.TotalSleepMinutes]   = HigherIsBetter,
        [MetricKeys.DeepSleepMinutes]    = HigherIsBetter,
        [MetricKeys.RemSleepMinutes]     = HigherIsBetter,
        [MetricKeys.SleepEfficiency]     = HigherIsBetter,
        [MetricKeys.SleepScore]          = HigherIsBetter,
        [MetricKeys.ReadinessScore]      = HigherIsBetter,
        [MetricKeys.ActivityScore]       = HigherIsBetter,
        [MetricKeys.Spo2Average]         = HigherIsBetter,
        [MetricKeys.Vo2Max]              = HigherIsBetter,
        [MetricKeys.Hdl]                 = HigherIsBetter,

        // Deliberately neutral: more steps is not better without knowing why they rose,
        // and TSH and vitamin D are bad at both ends.
        [MetricKeys.Steps]               = Neutral,
        [MetricKeys.ActiveCalories]      = Neutral,
        [MetricKeys.Tsh]                 = Neutral,
        [MetricKeys.VitaminD]            = Neutral,
    };

    // Unknown metrics -- a lab analyte nobody enumerated -- are neutral. An unknown
    // direction must never be guessed at: the only safe reading of "I don't know which
    // way is bad" is to treat the change as ordinary.
    public static int Polarity(string metric) =>
        Polarities.TryGetValue(metric, out var p) ? p : Neutral;

    // direction is a regime change's own word for the step: "up" or "down".
    public static bool IsAdverse(string metric, string direction)
    {
        var polarity = Polarity(metric);
        if (polarity == Neutral) return false;
        return direction == "up" ? polarity == HigherIsWorse : polarity == HigherIsBetter;
    }

    public static bool IsKnown(string metric) => Polarities.ContainsKey(metric);
}
