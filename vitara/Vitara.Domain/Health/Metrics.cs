namespace Vitara.Domain.Health;

// The canonical names for everything Vitara measures.
//
// String keys rather than an enum: lab analytes arrive with names nobody enumerated in
// advance, and a metric that cannot be stored because the enum lacks a member is a
// metric that is lost. Known ones are normalised to these constants; unknown ones are
// stored under their own name and can be promoted later.
public static class MetricKeys
{
    // Dense tier — Oura, daily.
    public const string RestingHeartRate = "resting_hr";
    public const string HrvRmssd = "hrv_rmssd";
    public const string SkinTempDeviation = "skin_temp_deviation";
    public const string SleepScore = "sleep_score";
    public const string TotalSleepMinutes = "total_sleep_minutes";
    public const string DeepSleepMinutes = "deep_sleep_minutes";
    public const string RemSleepMinutes = "rem_sleep_minutes";
    public const string SleepEfficiency = "sleep_efficiency";
    public const string BreathingRate = "breathing_rate";
    public const string Spo2Average = "spo2_average";
    public const string ReadinessScore = "readiness_score";
    public const string ActivityScore = "activity_score";
    public const string Steps = "steps";
    public const string ActiveCalories = "active_calories";
    public const string StressHighSeconds = "stress_high_seconds";
    public const string Vo2Max = "vo2_max";
    public const string CardiovascularAge = "cardiovascular_age";

    // Medium tier — entered by hand.
    public const string SystolicBp = "systolic_bp";
    public const string DiastolicBp = "diastolic_bp";
    public const string Pulse = "pulse";
    public const string WeightKg = "weight_kg";
    public const string Glucose = "glucose";

    // Sparse tier — labs.
    public const string Hba1c = "hba1c";
    public const string TotalCholesterol = "total_cholesterol";
    public const string Ldl = "ldl";
    public const string Hdl = "hdl";
    public const string Triglycerides = "triglycerides";
    public const string Crp = "crp";
    public const string Tsh = "tsh";
    public const string VitaminD = "vitamin_d";
}

public static class Tiers
{
    // Daily, from a device. Baselines, z-scores and trends are all valid here.
    public const string Dense = "dense";

    // Entered by hand every few days. Baselines work; trends need wider windows.
    public const string Medium = "medium";

    // Labs, roughly twice a year. Compared to the previous draw and to a reference
    // range -- never fitted, never projected. Four points over two years is a sequence
    // to annotate, not a trend.
    public const string Sparse = "sparse";
}

// How a reading was taken. Part of the measurement's identity, not decoration:
// fasting and post-meal glucose are different quantities that happen to share a name,
// and pooling them produces a baseline describing neither.
public record MeasurementContext(
    bool? Fasting = null,
    string? TimeOfDay = null,        // waking | morning | afternoon | evening | night
    string? Position = null,         // supine | seated | standing
    string? Arm = null,              // left | right
    string? CuffSize = null,
    string? PrePostExercise = null,
    double? CaffeineWithinHours = null,
    bool? AlcoholPreviousEvening = null);

// Which parts of the context actually key a baseline, per metric.
//
// THE IMPORTANT DESIGN DECISION HERE. The obvious reading of "baselines are computed
// per (metric, context signature)" is to use every context field -- and that is
// unusable arithmetic. Blood pressure keyed on position, arm, fasting, time of day,
// caffeine and alcohol is dozens of signatures, each needing ~30 readings before it
// says anything. Nobody accumulates thirty morning-seated-left-arm-no-caffeine-
// no-alcohol-fasted readings. Every bucket stays permanently invalid and the feature
// silently never works.
//
// So each metric declares only the context that genuinely changes the number enough to
// be worth splitting on. Everything else is recorded on the observation -- so it is
// there to explain an outlier later -- but does not fragment the baseline.
public static class BaselineKeys
{
    private static readonly Dictionary<string, string[]> Fields = new()
    {
        // Posture and time of day move blood pressure enough to matter. Arm and cuff
        // size are recorded but do not split: the difference between arms is smaller
        // than the cost of halving the sample.
        [MetricKeys.SystolicBp] = ["position", "timeOfDay"],
        [MetricKeys.DiastolicBp] = ["position", "timeOfDay"],
        [MetricKeys.Pulse] = ["position", "timeOfDay"],

        // Fasting or not is the whole interpretation of a glucose reading. Nothing else
        // comes close, and splitting further would leave neither half usable.
        [MetricKeys.Glucose] = ["fasting"],

        // Weight varies with time of day more than most people expect, but a single
        // habitual weighing time means one bucket in practice.
        [MetricKeys.WeightKg] = ["timeOfDay"],
    };

    // Oura metrics get no context split at all: they are machine-measured overnight
    // under conditions the user does not vary.
    public static string[] For(string metric) => Fields.TryGetValue(metric, out var f) ? f : [];

    // A stable string identifying which baseline a reading belongs to. Same metric and
    // same relevant context always produces the same signature, so it can be compared
    // and stored rather than recomputed everywhere.
    public static string Signature(string metric, MeasurementContext? context)
    {
        var fields = For(metric);
        if (fields.Length == 0 || context is null) return "";

        var parts = fields.Select(f => f switch
        {
            "position" => $"position={context.Position ?? "?"}",
            "timeOfDay" => $"timeOfDay={context.TimeOfDay ?? "?"}",
            "fasting" => $"fasting={(context.Fasting is { } b ? b.ToString().ToLowerInvariant() : "?")}",
            "arm" => $"arm={context.Arm ?? "?"}",
            _ => $"{f}=?",
        });

        return string.Join("|", parts);
    }

    // A reading missing the context its metric baselines on is stored, shown, and kept
    // out of the baseline. Pooling it would quietly corrupt the very number it was
    // meant to contribute to -- a glucose reading of unknown fasting state belongs to
    // neither bucket.
    public static bool CanBaseline(string metric, MeasurementContext? context)
    {
        var fields = For(metric);
        if (fields.Length == 0) return true;
        if (context is null) return false;

        return fields.All(f => f switch
        {
            "position" => context.Position is not null,
            "timeOfDay" => context.TimeOfDay is not null,
            "fasting" => context.Fasting is not null,
            "arm" => context.Arm is not null,
            _ => false,
        });
    }
}
