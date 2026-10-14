namespace Vitara.Domain.Health;

// What to strive for, per measure: a healthy range drawn from longevity research, next to
// (never instead of) the person's own normal (Vitara ADR 0028).
//
// Two different questions, kept apart on purpose. "Your normal" says whether today is unusual
// FOR YOU, and is what every finding is measured against. "Strive for" says where people who
// live longer and healthier tend to sit, so a normal that is steady but high (a resting heart
// rate that has always been 78) is visible as something worth moving.
//
// Rules for an entry:
//   - Every range names its source and carries an evidence grade (A trials or large
//     meta-analyses agree; B consistent large observational studies; C suggestive).
//   - Longevity-leaning but not tight: where guidelines and "optimal" voices disagree, the
//     guideline's healthy end is used, not the most aggressive target.
//   - South Asian cut-offs where they differ (waist), because Vitara's first users are there.
//   - A range that needs age or sex and does not have it is not guessed: it is absent, with
//     the reason.
//   - Nothing here is a diagnosis or a treatment target. The wording on every surface says
//     "strive for", and labs keep the lab's own printed range as the clinical reference.
public record HealthyRange(
    string Metric,
    double? Low,          // strive for at least this (inclusive; null: no lower bound)
    double? High,         // strive for under this (exclusive, as guidelines say "under 120"; null: none)
    double? RiskBelow,    // under this the research sees clearly higher risk
    double? RiskAbove,    // at or over this likewise ("130 and over")
    string Grade,
    string Source,
    string Basis);        // one plain sentence: what the range is and where it comes from

public static class HealthyRanges
{
    // Measures where a population number would mislead: they are only meaningful against the
    // person's own history, and are said to be so rather than silently missing.
    public static readonly string[] PersonalOnly =
    [
        MetricKeys.HrvRmssd, MetricKeys.SkinTempDeviation, MetricKeys.BreathingRate,
        MetricKeys.ReadinessScore, MetricKeys.SleepScore, MetricKeys.ActivityScore,
    ];

    public const string PersonalOnlyNote =
        "No population target: this varies too much between people to have one. Compare it with your own normal.";

    // Why a range that needs age or sex is missing.
    public record Missing(string Metric, string Reason);

    public static (IReadOnlyList<HealthyRange> Ranges, IReadOnlyList<Missing> Missing) For(int? age, string? sex)
    {
        var female = Female(sex);
        var ranges = new List<HealthyRange>();
        var missing = new List<Missing>();

        void Add(HealthyRange r) => ranges.Add(r);

        // ── Daily, from the ring and the phone ───────────────────────────────

        Add(new(MetricKeys.RestingHeartRate, 50, 65, null, 75, "B",
            "Zhang et al., CMAJ 2016 (meta-analysis, 46 studies)",
            "All-cause mortality rose about 9% for every 10 bpm of resting heart rate; the lowest risk sat roughly between 50 and 65."));

        // Steps: the benefit levels off at a lower count from 60 on.
        if (age is { } a)
            Add(new(MetricKeys.Steps, a >= 60 ? 6000 : 8000, null, 5000, null, "A",
                "Paluch et al., Lancet Public Health 2022 (meta-analysis, 15 cohorts)",
                a >= 60
                    ? "From 60 on, the drop in mortality levelled off at about 6,000 to 8,000 steps a day. More is not harmful; it just adds less."
                    : "Under 60, the drop in mortality levelled off at about 8,000 to 10,000 steps a day. More is not harmful; it just adds less."));
        else
            Add(new(MetricKeys.Steps, 8000, null, 5000, null, "A",
                "Paluch et al., Lancet Public Health 2022 (meta-analysis, 15 cohorts)",
                "The drop in mortality levelled off at about 8,000 to 10,000 steps a day (6,000 to 8,000 from age 60; add your age to the profile to get that one)."));

        Add(new(MetricKeys.TotalSleepMinutes, 420, 540, 360, 600, "B",
            "Cappuccio et al., Sleep 2010 (meta-analysis); AASM/SRS consensus 2015",
            "Seven to nine hours asleep. Under six went with higher mortality, and so did regularly sleeping ten or more."));

        Add(new(MetricCatalogue.BedtimeVariabilityMinutes, null, 30, null, 60, "B",
            "Windred et al., Sleep 2024 (UK Biobank, 60,000 people)",
            "Regular sleep timing predicted mortality better than sleep length. Measured there as a regularity index; here as how much your bedtime varies, so the line is approximate."));

        Add(new(MetricKeys.Spo2Average, 95, 100, 92, null, "B",
            "Clinical normal range for blood oxygen at rest",
            "Ninety-five per cent or more overnight. A ring's reading is an estimate; repeated low nights are worth asking about."));

        // VO2 max: at or above the 75th percentile for age and sex, from measured treadmill tests.
        if (Vo2Percentiles(age, female) is { } v)
            Add(new(MetricKeys.Vo2Max, v.P75, null, v.P25, null, "A",
                "Kaminsky et al., Mayo Clin Proc 2015 (FRIEND registry); Mandsager et al., JAMA Netw Open 2018",
                $"Top quarter for your age and sex (from {v.P75:0.#}). Fitness was among the strongest predictors of a longer life, with no upper limit to the benefit; the bottom quarter (under {v.P25:0.#}) carried the highest risk. A ring's figure is an estimate, not a lab test."));
        else
            missing.Add(new(MetricKeys.Vo2Max, "Needs your age and sex in the profile: fitness norms differ by both."));

        // ── Body ─────────────────────────────────────────────────────────────

        Add(new(MetricKeys.SystolicBp, null, 120, null, 130, "A",
            "SPRINT trial, NEJM 2015; ACC/AHA guideline 2017",
            "Under 120. From 130 the guideline calls it high; the SPRINT trial found fewer heart events aiming under 120."));
        Add(new(MetricKeys.DiastolicBp, null, 80, null, 80, "A",
            "ACC/AHA guideline 2017",
            "Under 80; from 80 the guideline calls it high."));

        if (female is { } fw)
            Add(new(MetricKeys.WaistCircumferenceCm, null, fw ? 80 : 90, null, fw ? 80 : 90, "B",
                "International Diabetes Federation, South Asian cut-offs (2006)",
                fw ? "Under 80 cm, the cut-off for South Asian women: risk of diabetes and heart disease rises above it at a smaller waist than in Europeans."
                   : "Under 90 cm, the cut-off for South Asian men: risk of diabetes and heart disease rises above it at a smaller waist than in Europeans."));
        else
            missing.Add(new(MetricKeys.WaistCircumferenceCm, "Needs your sex in the profile: the cut-off differs for men and women."));

        // Grip: one standard deviation under the British peak mean as the line to stay above, and
        // the weakness cut-off (Dodds T-score -2.5, the same as EWGSOP2) as the risk line.
        if (female is { } fg)
            Add(new(MetricKeys.GripStrengthKg, fg ? 25 : 42, null, fg ? 16 : 27, null, "B",
                "Leong et al., Lancet 2015 (PURE, 140,000 people); Dodds et al., PLoS ONE 2014 (norms)",
                $"Above {(fg ? 25 : 42)} kg, within one standard deviation of peak adult strength. Every 5 kg lower went with about 16% higher mortality; under {(fg ? 16 : 27)} kg counts as weak."));
        else
            missing.Add(new(MetricKeys.GripStrengthKg, "Needs your sex in the profile: grip norms differ for men and women."));

        // ── Labs ─────────────────────────────────────────────────────────────

        Add(new(MetricKeys.Ldl, null, 100, null, 130, "A",
            "ESC/EAS dyslipidaemia guideline 2019; Ference et al., Eur Heart J 2017 (Mendelian randomisation)",
            "Under 100 mg/dL (under 70 if you are at high risk, which your doctor decides). Lower for longer means less artery disease."));
        Add(new(MetricKeys.ApoB, null, 90, null, 100, "A",
            "ESC/EAS dyslipidaemia guideline 2019",
            "Under 90 mg/dL (under 80 at high risk). ApoB counts the particles that cause artery disease."));
        Add(new(MetricKeys.Triglycerides, null, 100, null, 150, "B",
            "AHA scientific statement on triglycerides, Circulation 2011",
            "Under 100 mg/dL fasting is optimal; 150 and over is high."));
        if (female is { } fh)
            Add(new(MetricKeys.Hdl, fh ? 50 : 40, 90, fh ? 50 : 40, 90, "B",
                "Madsen et al., Eur Heart J 2017 (Copenhagen studies)",
                $"At least {(fh ? 50 : 40)} mg/dL. Very high HDL (over about 90) was not protective: risk rose again, so more is not always better."));
        else
            missing.Add(new(MetricKeys.Hdl, "Needs your sex in the profile: the healthy line differs for men and women."));
        Add(new(MetricKeys.Hba1c, null, 5.5, null, 5.7, "A",
            "Selvin et al., NEJM 2010 (ARIC, 11,000 adults); ADA Standards of Care",
            "Under 5.5%: the lowest risk sat at 5.0 to 5.5. From 5.7 the ADA calls it prediabetes."));
        Add(new(MetricKeys.Glucose, 70, 100, 70, 100, "A",
            "ADA Standards of Care",
            "70 to 99 mg/dL fasting. Readings after food run higher and are not compared with this."));
        Add(new(MetricKeys.FastingInsulin, null, 10, null, 15, "C",
            "No guideline; commonly used research cut-offs, and assays differ between labs",
            "Under about 10 µIU/mL. A rough line only: read it with glucose and HbA1c."));
        Add(new(MetricKeys.HsCrp, null, 1, null, 3, "B",
            "AHA/CDC scientific statement, Circulation 2003",
            "Under 1 mg/L is low cardiovascular risk; over 3 is high. A cold or injury raises it for days, so one high reading means little."));
        Add(new(MetricKeys.Egfr, 90, null, 60, null, "A",
            "KDIGO chronic kidney disease guideline 2012",
            "90 or above is normal kidney filtering; under 60 for three months is chronic kidney disease. It falls slowly with age."));
        Add(new(MetricKeys.VitaminD, 30, 50, 20, null, "C",
            "Endocrine Society guideline 2011; its 2024 update no longer recommends a target",
            "30 to 50 ng/mL, shown softly: the latest guideline found too little evidence to set a target for most adults."));

        return (ranges, missing);
    }

    // FRIEND registry, measured treadmill VO2max, mL/kg/min (Kaminsky 2015, table 1).
    private static readonly (int From, double M25, double M75, double F25, double F75)[] Friend =
    [
        (20, 40.1, 55.2, 30.5, 44.7),
        (30, 35.9, 49.2, 25.3, 36.1),
        (40, 31.9, 45.0, 22.1, 32.4),
        (50, 27.1, 39.7, 19.9, 27.6),
        (60, 23.7, 34.5, 17.2, 23.8),
        (70, 20.4, 30.4, 15.6, 20.8),
    ];

    public static (double P25, double P75)? Vo2Percentiles(int? age, bool? female)
    {
        if (age is not { } a || female is not { } f || a < 18) return null;
        var row = Friend.Last(r => r.From <= Math.Max(20, Math.Min(a, 79)));
        return f ? (row.F25, row.F75) : (row.M25, row.M75);
    }

    // Where a value sits against a range: "inside", "below", "above", and separately whether
    // it has crossed the risk line. Null bounds are open. "Above" is not always bad (VO2 max,
    // steps and grip have no upper bound for that reason).
    public static string Position(HealthyRange r, double value) =>
        r.Low is { } lo && value < lo ? "below"
        : r.High is { } hi && value >= hi ? "above"
        : "inside";

    public static bool AtRisk(HealthyRange r, double value) =>
        (r.RiskBelow is { } rb && value < rb) || (r.RiskAbove is { } ra && value >= ra);

    private static bool? Female(string? sex) => sex?.Trim().ToLowerInvariant() switch
    {
        "female" or "f" or "woman" => true,
        "male" or "m" or "man" => false,
        _ => null,
    };
}
