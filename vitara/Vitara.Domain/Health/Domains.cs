namespace Vitara.Domain.Health;

// One area of health, and the measures that describe it.
//
// KeyTests are the measures without which the domain cannot answer its own question --
// metabolic health without fasting insulin cannot see insulin resistance building, body
// composition without lean mass cannot tell a good loss from a bad one. Listed so a gap
// is named, rather than discovered by noticing that nothing was said.
public record HealthDomain(
    string Key,
    string Label,
    string Question,
    string[] Metrics,
    string[][] KeyTests,          // each inner list is "any one of these"
    string[]? Refers = null);     // other domains read alongside, never re-counted

// The domains health is read in (Vitara ADR 0019).
//
// A metric may sit in several: triglycerides are cardiovascular and metabolic, resting
// heart rate is cardiovascular, fitness and recovery. It is shown in each, and a finding
// is listed once per domain. What no domain ever gets is a score -- see DomainView.
public static class Domains
{
    private static readonly string[] AnyStrength =
        [MetricKeys.GripStrengthKg, MetricKeys.SitToStand30s, MetricKeys.PushupsMax, MetricKeys.Strength1RmKg];

    public static readonly IReadOnlyList<HealthDomain> All =
    [
        new("cardiovascular", "Heart and circulation",
            "How the heart and blood vessels are doing, from pressure, lipids and fitness.",
            [
                MetricKeys.SystolicBp, MetricKeys.DiastolicBp, MetricKeys.Pulse, MetricKeys.RestingHeartRate,
                MetricKeys.HrvRmssd, MetricKeys.TotalCholesterol, MetricKeys.Ldl, MetricKeys.Hdl,
                MetricKeys.Triglycerides, MetricKeys.NonHdl, MetricKeys.ApoB, MetricKeys.Lpa,
                MetricKeys.Vo2Max, MetricKeys.Steps, MetricKeys.ActiveCalories, MetricKeys.CardiovascularAge,
            ],
            [[MetricKeys.SystolicBp], [MetricKeys.Ldl, MetricKeys.ApoB, MetricKeys.NonHdl]]),

        new("metabolic", "Metabolic health",
            "How the body handles energy: glucose, insulin, and the fat that carries risk.",
            [
                MetricKeys.Glucose, MetricKeys.Hba1c, MetricKeys.FastingInsulin, MetricKeys.HomaIr,
                MetricKeys.Triglycerides, MetricKeys.WaistCircumferenceCm, MetricKeys.WeightKg,
                MetricKeys.BodyFatPct, MetricKeys.Alt,
            ],
            [[MetricKeys.FastingInsulin], [MetricKeys.Hba1c, MetricKeys.Glucose]]),

        new("body_composition", "Body composition",
            "What the body is made of, not just what it weighs.",
            [
                MetricKeys.WeightKg, MetricKeys.WaistCircumferenceCm, MetricCatalogue.Bmi,
                MetricCatalogue.WaistToHeight, MetricKeys.BodyFatPct, MetricKeys.LeanMassKg, MetricKeys.VisceralFat,
            ],
            [[MetricKeys.LeanMassKg], [MetricKeys.WaistCircumferenceCm]]),

        new("fitness", "Fitness and strength",
            "What the body can do: aerobic capacity, strength and daily movement.",
            [
                MetricKeys.Vo2Max, MetricKeys.Steps, MetricKeys.ActiveCalories, MetricKeys.ActivityScore,
                MetricCatalogue.AcwrActiveCalories, MetricKeys.RestingHeartRate,
                MetricKeys.GripStrengthKg, MetricKeys.SitToStand30s, MetricKeys.PushupsMax, MetricKeys.Strength1RmKg,
            ],
            [AnyStrength, [MetricKeys.Vo2Max]]),

        new("sleep_and_recovery", "Sleep and recovery",
            "How much, how well and how regularly you sleep, and how recovered the body is.",
            [
                MetricKeys.TotalSleepMinutes, MetricKeys.DeepSleepMinutes, MetricKeys.RemSleepMinutes,
                MetricKeys.SleepEfficiency, MetricKeys.SleepScore, MetricKeys.BedtimeMinutes, MetricKeys.WakeMinutes,
                MetricCatalogue.BedtimeVariabilityMinutes, MetricCatalogue.SleepDebtMinutes,
                MetricKeys.HrvRmssd, MetricKeys.RestingHeartRate, MetricKeys.SkinTempDeviation,
                MetricKeys.BreathingRate, MetricKeys.Spo2Average, MetricKeys.ReadinessScore, MetricKeys.StressHighSeconds,
            ],
            [[MetricKeys.TotalSleepMinutes]]),

        new("nutrition", "Nutrition timing",
            "The habits around eating and drinking that move tomorrow's recovery.",
            [
                MetricKeys.AlcoholUnits, MetricKeys.CaffeineLastTime, MetricKeys.LastMealTime,
                MetricCatalogue.CaffeineHoursBeforeBed, MetricCatalogue.LastMealHoursBeforeBed,
            ],
            [[MetricKeys.AlcoholUnits]]),

        new("inflammation", "Inflammation",
            "Low-grade inflammation, which travels with most of the other domains.",
            [MetricKeys.HsCrp, MetricKeys.Crp],
            [[MetricKeys.HsCrp, MetricKeys.Crp]]),

        new("nutritional_status", "Nutritional status",
            "Vitamins and iron: the stores that run down quietly before anything is felt.",
            [
                MetricKeys.VitaminD, MetricKeys.VitaminB12, MetricKeys.Ferritin, MetricKeys.Iron,
                MetricKeys.Folate, MetricKeys.Tibc, MetricKeys.TransferrinSaturation,
            ],
            [[MetricKeys.Ferritin], [MetricKeys.VitaminD], [MetricKeys.VitaminB12]]),

        new("organ_health", "Kidney, liver and thyroid",
            "The organs whose function shows up in a routine blood panel.",
            [MetricKeys.Egfr, MetricKeys.Creatinine, MetricKeys.Alt, MetricKeys.Ast, MetricKeys.Tsh],
            [[MetricKeys.Egfr, MetricKeys.Creatinine], [MetricKeys.Alt]]),

        // Ageing well is the other domains read together, plus physical function. It
        // lists only the function measures as its own, so nothing is counted twice.
        new("healthy_aging", "Ageing well",
            "Strength, function and the other domains together: what keeps someone independent.",
            [MetricKeys.GripStrengthKg, MetricKeys.SitToStand30s],
            [[MetricKeys.GripStrengthKg, MetricKeys.SitToStand30s]],
            Refers: ["fitness", "body_composition", "metabolic", "cardiovascular", "sleep_and_recovery"]),
    ];

    public static HealthDomain? Find(string key) => All.FirstOrDefault(d => d.Key == key);

    public static IEnumerable<HealthDomain> Containing(string metric) => All.Where(d => d.Metrics.Contains(metric));
}
