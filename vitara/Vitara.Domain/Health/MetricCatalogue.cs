namespace Vitara.Domain.Health;

// Everything Vitara can know about you, named once.
//
// Written because "show me everything you track" had no answer: the UI could only show
// metrics that happened to have data, so a metric with no readings was indistinguishable
// from a metric that does not exist. A person deciding whether this is worth using needs
// to see the whole surface -- including the empty parts, and what would fill them.
//
// Labels and descriptions live here rather than in the web app so every surface says the
// same thing about the same number.
public record MetricInfo(
    string Key,
    string Label,
    string Unit,
    string Group,        // Sleep | Recovery | Activity | Body | Vitals | Labs
    string Tier,         // Tiers: dense | medium | sparse
    string Source,       // ring | phone | manual | lab | computed
    string What,         // one plain sentence: what it is and why it is here
    int Decimals = 0,
    int StaleAfterDays = 3)
{
    public bool Computed => Source == "computed";

    // +1 higher is better, -1 higher is worse, 0 neither. Carried so a client can colour
    // a change without re-deciding what "better" means.
    public int Polarity => MetricDirection.Polarity(Key);
}

public static class MetricCatalogue
{
    public const string GroupSleep = "Sleep";
    public const string GroupRecovery = "Recovery";
    public const string GroupActivity = "Activity";
    public const string GroupBody = "Body";
    public const string GroupVitals = "Vitals";
    public const string GroupLabs = "Labs";

    // Derived values are metrics too -- they are computed rather than measured, and the
    // distinction belongs in the row, not in whether it appears at all.
    public const string SleepDebtMinutes = "sleep_debt_minutes";
    public const string AcwrActiveCalories = "acwr_active_calories";
    public const string Bmi = "bmi";
    public const string WaistToHeight = "waist_to_height";

    public static readonly IReadOnlyList<MetricInfo> All =
    [
        // ── Sleep ───────────────────────────────────────────────────────────────
        new(MetricKeys.TotalSleepMinutes, "Time asleep", "min", GroupSleep, Tiers.Dense, "ring",
            "How long you actually slept, awake time removed. The single number most other things follow."),
        new(MetricKeys.DeepSleepMinutes, "Deep sleep", "min", GroupSleep, Tiers.Dense, "ring",
            "The physically restorative stage. Usually the first thing a late night or a drink takes away."),
        new(MetricKeys.RemSleepMinutes, "REM sleep", "min", GroupSleep, Tiers.Dense, "ring",
            "The dreaming stage, tied to memory and mood. Alcohol and late meals push it later and shorter."),
        new(MetricKeys.SleepEfficiency, "Sleep efficiency", "%", GroupSleep, Tiers.Dense, "ring",
            "The share of time in bed you were asleep. Low with long sleep usually means restless nights, not short ones.", 1),
        new(MetricKeys.SleepScore, "Sleep score", "/100", GroupSleep, Tiers.Dense, "ring",
            "Your ring's own summary of the night. Kept because it is familiar, though the parts above say more."),
        new(SleepDebtMinutes, "Sleep debt", "min", GroupSleep, Tiers.Dense, "computed",
            "How far behind your own sleep need you are over the last two weeks. Built from your nights, not a general recommendation."),

        // ── Recovery ────────────────────────────────────────────────────────────
        new(MetricKeys.RestingHeartRate, "Resting heart rate", "bpm", GroupRecovery, Tiers.Dense, "ring",
            "Your lowest heart rate overnight. Rises with illness, alcohol, heat and stress, often before you feel any of them."),
        new(MetricKeys.HrvRmssd, "Heart rate variability", "ms", GroupRecovery, Tiers.Dense, "ring",
            "The variation between heartbeats overnight. Higher usually means better recovered; it is personal, so only your own range matters.", 1),
        new(MetricKeys.SkinTempDeviation, "Skin temperature", "°C", GroupRecovery, Tiers.Dense, "ring",
            "How far your overnight skin temperature sat from your own usual — the ring reports a difference, not a temperature. One of the earliest signs of coming illness, and only a sustained rise of at least a tenth of a degree is treated as one.", 2),
        new(MetricKeys.BreathingRate, "Breathing rate", "/min", GroupRecovery, Tiers.Dense, "ring",
            "Breaths per minute while asleep. Very stable normally, which is what makes a change worth noticing.", 1),
        new(MetricKeys.Spo2Average, "Blood oxygen", "%", GroupRecovery, Tiers.Dense, "ring",
            "Average overnight oxygen saturation. Persistent dips can point at breathing disturbance in the night.", 1),
        new(MetricKeys.ReadinessScore, "Readiness", "/100", GroupRecovery, Tiers.Dense, "ring",
            "Your ring's summary of how recovered you are today."),
        new(MetricKeys.StressHighSeconds, "High-stress time", "s", GroupRecovery, Tiers.Dense, "ring",
            "Time your body spent in a high-stress state during the day, as read from heart rate and skin signals."),

        // ── Activity ────────────────────────────────────────────────────────────
        new(MetricKeys.Steps, "Steps", "steps", GroupActivity, Tiers.Dense, "ring",
            "Steps taken. Coarse, but the most comparable day-to-day measure of simply moving."),
        new(MetricKeys.ActiveCalories, "Active calories", "kcal", GroupActivity, Tiers.Dense, "ring",
            "Energy burned beyond resting. The input behind your training-load balance."),
        new(MetricKeys.ActivityScore, "Activity score", "/100", GroupActivity, Tiers.Dense, "ring",
            "Your ring's summary of the day's movement."),
        new(AcwrActiveCalories, "Training load balance", "ratio", GroupActivity, Tiers.Dense, "computed",
            "This week's training against the last month's. Around 1.0 is steady; well above means you ramped up faster than you adapted.", 2),
        new(MetricKeys.Vo2Max, "VO₂ max", "ml/kg/min", GroupActivity, Tiers.Dense, "ring",
            "An estimate of aerobic fitness. Moves slowly, so a month is the shortest meaningful comparison.", 1, StaleAfterDays: 14),

        // ── Body ────────────────────────────────────────────────────────────────
        new(MetricKeys.WeightKg, "Weight", "kg", GroupBody, Tiers.Medium, "manual",
            "Weighed by you, or synced from a scale or phone. Time of day matters, so it is baselined per time of day.", 1, StaleAfterDays: 7),
        new(Bmi, "BMI", "", GroupBody, Tiers.Medium, "computed",
            "Weight against height. Included because it is expected; waist-to-height below is the better guide.", 1, StaleAfterDays: 7),
        new(MetricKeys.WaistCircumferenceCm, "Waist", "cm", GroupBody, Tiers.Medium, "manual",
            "Measured with a tape at the navel. Needs you; nothing can collect it automatically.", 1, StaleAfterDays: 30),
        new(WaistToHeight, "Waist-to-height", "ratio", GroupBody, Tiers.Medium, "computed",
            "Waist divided by height. A better guide to metabolic risk than BMI, and it needs one tape measurement.", 2, StaleAfterDays: 30),
        new(MetricKeys.CardiovascularAge, "Cardiovascular age", "years", GroupBody, Tiers.Dense, "ring",
            "Your ring's estimate of circulatory age against your years. An estimate, not a diagnosis.", 1, StaleAfterDays: 14),

        // ── Vitals ──────────────────────────────────────────────────────────────
        new(MetricKeys.SystolicBp, "Blood pressure (systolic)", "mmHg", GroupVitals, Tiers.Medium, "manual",
            "The upper number. Recorded with how you were sitting and when, because both change it.", 0, StaleAfterDays: 14),
        new(MetricKeys.DiastolicBp, "Blood pressure (diastolic)", "mmHg", GroupVitals, Tiers.Medium, "manual",
            "The lower number, from the same reading.", 0, StaleAfterDays: 14),
        new(MetricKeys.Pulse, "Pulse at the cuff", "bpm", GroupVitals, Tiers.Medium, "manual",
            "The heart rate your blood-pressure monitor reported. Seated and awake, so it reads higher than your overnight resting rate.", 0, StaleAfterDays: 14),
        new(MetricKeys.Glucose, "Blood glucose", "mg/dL", GroupVitals, Tiers.Medium, "manual",
            "Fasting and post-meal readings are different questions, so they are kept in separate ranges.", 0, StaleAfterDays: 14),

        // ── Labs ────────────────────────────────────────────────────────────────
        new(MetricKeys.Hba1c, "HbA1c", "%", GroupLabs, Tiers.Sparse, "lab",
            "Average blood sugar over about three months. Anchors the wearable picture to something measured in a lab.", 1, StaleAfterDays: 240),
        new(MetricKeys.TotalCholesterol, "Total cholesterol", "mg/dL", GroupLabs, Tiers.Sparse, "lab",
            "From a lipid panel. Read alongside LDL, HDL and triglycerides rather than on its own.", 0, StaleAfterDays: 240),
        new(MetricKeys.Ldl, "LDL cholesterol", "mg/dL", GroupLabs, Tiers.Sparse, "lab",
            "The one most cardiovascular guidance is written around.", 0, StaleAfterDays: 240),
        new(MetricKeys.Hdl, "HDL cholesterol", "mg/dL", GroupLabs, Tiers.Sparse, "lab",
            "Higher is the favourable direction here, unlike the rest of the panel.", 0, StaleAfterDays: 240),
        new(MetricKeys.Triglycerides, "Triglycerides", "mg/dL", GroupLabs, Tiers.Sparse, "lab",
            "Blood fats, sensitive to recent eating and drinking as well as to the longer trend.", 0, StaleAfterDays: 240),
        new(MetricKeys.Crp, "CRP", "mg/L", GroupLabs, Tiers.Sparse, "lab",
            "A general marker of inflammation.", 1, StaleAfterDays: 240),
        new(MetricKeys.Tsh, "TSH", "mIU/L", GroupLabs, Tiers.Sparse, "lab",
            "Thyroid signalling. Out of range in either direction is worth a conversation.", 2, StaleAfterDays: 240),
        new(MetricKeys.VitaminD, "Vitamin D", "ng/mL", GroupLabs, Tiers.Sparse, "lab",
            "Commonly low in winter and indoors; both ends of the range matter.", 0, StaleAfterDays: 240),
    ];

    public static readonly IReadOnlyList<string> Groups =
        [GroupSleep, GroupRecovery, GroupActivity, GroupBody, GroupVitals, GroupLabs];

    public static MetricInfo? Find(string key) => All.FirstOrDefault(m => m.Key == key);
}
