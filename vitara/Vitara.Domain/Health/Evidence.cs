namespace Vitara.Domain.Health;

// How well established each thing this system says actually is.
//
// Vitara computes numbers of four very different kinds and, until now, presented them
// in one voice. A blood pressure reading against a published range and a biological
// age estimate are not the same sort of claim, and the only thing separating them in
// the UI was the wording of a sentence somebody wrote once.
//
// THE FAILURE THIS PREVENTS IS SPECIFIC. Longevity software converges on the same bad
// habit: a handful of genuinely established measures get surrounded by fashionable
// derived scores, everything is rendered in the same card with the same confidence,
// and the user ends up weighting an undisclosed vendor estimate exactly as heavily as
// their LDL. The established numbers are not made better by the company they keep --
// the experimental ones are made to look like medicine.
//
// So the grade travels with the claim, it is attached in code rather than in copy, and
// a concept with no grade is a bug a test fails on. The grade is about the STRENGTH OF
// THE EVIDENCE, not about confidence in the measurement: a well-measured number
// resting on a contested theory is still a C.
public enum Grade
{
    // Established. Large, replicated, outcome-based evidence, and in clinical use.
    // Reference ranges, blood pressure, LDL, HbA1c.
    A,

    // Strong, but context-dependent. The association is solid and the interpretation
    // depends on who you are -- or the measure is validated at population level and
    // noisy for one person on one day.
    B,

    // Emerging. Real literature, genuine disagreement, no settled threshold. Most
    // wearable-derived indices live here.
    C,

    // Experimental. A plausible construct with no validated mapping to outcomes. Our
    // own composites and undisclosed vendor estimates.
    D,
}

// One graded claim.
//
// Caveat is not optional and is not decoration: it is the sentence that stops the
// grade from being read as a star rating. A grade of B with no stated context is just
// a B-shaped endorsement.
public record EvidenceNote(string Key, Grade Grade, string Claim, string Basis, string Caveat)
{
    public string Label => Evidence.Label(Grade);

    // Whether the claim must be presented with its caveat attached rather than on its
    // own. Everything emerging or experimental must; established things may be stated
    // plainly.
    public bool MustQualify => Grade is Grade.C or Grade.D;
}

public static class Evidence
{
    public static string Label(Grade grade) => grade switch
    {
        Grade.A => "established",
        Grade.B => "strong, depends on context",
        Grade.C => "emerging",
        _ => "experimental",
    };

    // Concepts that are not metrics: methods, detectors and estimates. Named here so
    // the forecast and the illness detector can be graded as readily as a lab analyte.
    public const string Forecast = "forecast";
    public const string Correlation = "correlation";
    public const string IllnessDetection = "illness_detection";
    public const string BiologicalAge = "biological_age";
    public const string MetabolicPattern = "metabolic_pattern";
    public const string PersonalBaseline = "personal_baseline";
    public const string LabReferenceRange = "lab_reference_range";

    public static readonly IReadOnlyList<EvidenceNote> All =
    [
        // -- A: established ------------------------------------------------------
        new(MetricKeys.SystolicBp, Grade.A,
            "Blood pressure predicts cardiovascular events.",
            "Decades of randomised trials; treating it changes outcomes, which is the strongest form this evidence takes.",
            "A single reading decides nothing. The measurement is the average of several taken properly, seated and rested."),

        new(MetricKeys.DiastolicBp, Grade.A,
            "Blood pressure predicts cardiovascular events.",
            "As above; the two numbers come from one measurement and are graded together.",
            "Below about 50 the diastolic carries more of the signal, above about 60 the systolic does."),

        new(MetricKeys.Ldl, Grade.A,
            "LDL cholesterol is causally related to atherosclerotic disease.",
            "Consistent across observational data, randomised lipid-lowering trials and Mendelian randomisation -- the three agree, which is rare.",
            "The right target depends on your overall risk, which this system does not know. A number inside the printed range is not automatically the right number for you."),

        new(MetricKeys.Hba1c, Grade.A,
            "HbA1c reflects average glucose over roughly three months and tracks diabetes risk.",
            "The standard diagnostic and monitoring measure, with thresholds set by guideline bodies.",
            "Distorted by anything that changes red cell lifespan -- anaemia, recent blood loss, some haemoglobin variants -- and in those cases it reads low while glucose is not."),

        new(MetricKeys.Triglycerides, Grade.A,
            "Fasting triglycerides track metabolic and cardiovascular risk.",
            "Long-established part of the lipid panel.",
            "Very sensitive to the previous few days of eating and alcohol. A single high value after a weekend is not a trend."),

        new(MetricKeys.Hdl, Grade.A,
            "Low HDL is associated with higher cardiovascular risk.",
            "The association is established and consistent.",
            "The association is not causal: trials that raised HDL pharmacologically did not reduce events. Higher is favourable as a marker, not as a target to chase."),

        new(MetricKeys.NonHdl, Grade.A,
            "Non-HDL cholesterol measures all the cholesterol carried in atherogenic particles.",
            "Guideline-endorsed, at least as predictive as LDL, and does not require a fasting draw.",
            "Arithmetic from the panel rather than a separate assay, so it inherits any error in the two values behind it."),

        new(MetricKeys.WaistCircumferenceCm, Grade.A,
            "Waist circumference tracks the fat that carries metabolic risk.",
            "Predicts metabolic and cardiovascular outcomes independently of BMI.",
            "Measurement technique moves it by centimetres. Taken at a different height on the abdomen, it is a different number."),

        new(MetricCatalogue.WaistToHeight, Grade.A,
            "Waist divided by height above about 0.5 marks raised metabolic risk.",
            "Validated across many populations and more consistent across body sizes and ethnicities than BMI.",
            "A boundary, not a cliff. 0.49 and 0.51 are the same person."),

        new(MetricKeys.Egfr, Grade.A,
            "Estimated glomerular filtration rate is the standard measure of kidney function.",
            "CKD-EPI 2021 is the current recommended equation and is what kidney staging is built on.",
            "An estimate from creatinine, which is produced by muscle: it reads low in someone very muscular and high in someone with little muscle, and neither is a kidney finding."),

        // -- B: strong, context-dependent ----------------------------------------
        new(MetricKeys.ApoB, Grade.B,
            "ApoB counts the atherogenic particles directly and predicts risk at least as well as LDL.",
            "Strong and growing evidence; some guideline bodies now prefer it where it is available.",
            "Where LDL and ApoB disagree, ApoB is usually the better guide -- but the thresholds are less settled and fewer clinicians work to them."),

        new(MetricKeys.HomaIr, Grade.B,
            "HOMA-IR estimates insulin resistance from fasting glucose and fasting insulin.",
            "Validated against clamp studies at population level and widely used in research.",
            "Fasting insulin assays are not standardised between laboratories, so values do not transfer between labs and no single cut-off is universal. One value means little and the direction over several draws means more. It is not interpretable at all on insulin therapy."),

        new(MetricKeys.FastingInsulin, Grade.B,
            "Fasting insulin rises years before fasting glucose does.",
            "Consistent in longitudinal cohorts: compensatory hyperinsulinaemia precedes the glucose rise.",
            "Assay-dependent and variable from draw to draw. Only read alongside glucose and the trend."),

        new(MetricKeys.Lpa, Grade.B,
            "Lipoprotein(a) is a largely inherited, independent cardiovascular risk factor.",
            "Strong genetic and observational evidence that it raises risk.",
            "Almost entirely genetic and essentially fixed for life, and no treatment yet shows that lowering it reduces events. Measured once; repeating it is not monitoring."),

        new(MetricKeys.Vo2Max, Grade.B,
            "Cardiorespiratory fitness is among the strongest predictors of all-cause mortality.",
            "Large cohorts with measured fitness; the association is steepest at the low end.",
            "Graded for MEASURED VO2 max. The value here is a wrist-derived estimate from heart rate and pace, which tracks change within one person reasonably and should not be compared with anybody else's."),

        new(MetricKeys.HrvRmssd, Grade.B,
            "Overnight heart rate variability tracks autonomic state and recovery.",
            "Well-established physiology; it responds reliably to illness, alcohol and hard training.",
            "Absolute values are meaningless between people -- they vary several-fold with age and genetics. Only your own range and your own change say anything."),

        new(MetricKeys.RestingHeartRate, Grade.B,
            "A rising resting heart rate tracks illness, strain, alcohol and deconditioning.",
            "Clear physiology, and a solid association with fitness and outcomes.",
            "Moves with heat, altitude, caffeine and sleep timing. Read against your own baseline, never against a population figure."),

        new(MetricCatalogue.Bmi, Grade.B,
            "BMI tracks health risk across populations.",
            "Large-scale evidence at the population level.",
            "A poor individual measure: it cannot tell muscle from fat, and waist-to-height is the better guide for one person. Included because it is expected."),

        new(MetricKeys.Crp, Grade.B,
            "High-sensitivity CRP marks systemic inflammation and adds to cardiovascular risk prediction.",
            "Consistent association in large cohorts.",
            "Entirely non-specific, and raised for weeks by any infection, injury or dental problem. A single reading taken near an illness says nothing about baseline inflammation."),

        new(PersonalBaseline, Grade.B,
            "What is normal for one person, learned from their own readings.",
            "The right comparison for anything measured densely: between-person variation in HRV and resting heart rate dwarfs the within-person change being looked for.",
            "Needs enough readings to mean anything, and it moves. A baseline learned during a bad month makes the following normal month look like an improvement."),

        new(Correlation, Grade.B,
            "Rank correlation between a daily behaviour and a next-day response, with false-discovery control.",
            "The method is sound: curated pairs, Spearman, Benjamini-Hochberg across the whole run.",
            "The METHOD is strong; any individual relationship it reports is observational and uncontrolled. Anything that moves both at once -- being ill, being on holiday -- produces a real correlation and no causation."),

        // -- C: emerging ---------------------------------------------------------
        new(IllnessDetection, Grade.C,
            "Resting heart rate up, HRV down and skin temperature up together can precede symptoms.",
            "Repeatedly demonstrated in wearable cohorts, including at scale during COVID.",
            "Detects physiological disturbance, not infection. Alcohol, heat, a late hard session and a bad night produce the same pattern, and the thresholds here are tuned rather than validated."),

        new(MetricCatalogue.SleepDebtMinutes, Grade.C,
            "Accumulated shortfall against your own sleep need.",
            "Sleep restriction causes measurable harm, and a running deficit is a reasonable construct.",
            "The need it is measured against is itself estimated from your own nights, so this is a derived number resting on a derived number. Whether debt genuinely repays is not settled."),

        new(MetricCatalogue.AcwrActiveCalories, Grade.C,
            "Ramping training load faster than you adapt raises injury risk.",
            "Originally from team-sport injury data.",
            "The original work has been substantially criticised on statistical grounds and the 0.8-1.3 window is not an established threshold. Treated here as a prompt to look rather than a verdict."),

        new(MetricKeys.SkinTempDeviation, Grade.C,
            "Overnight skin temperature deviation is an early signal of physiological disturbance.",
            "Useful in wearable illness-detection work.",
            "A deviation from the ring's own reference rather than a temperature, at a resolution where room temperature and bedding matter. Not a fever reading."),

        new(Forecast, Grade.C,
            "Where a metric is heading if the current pattern continues.",
            "Ridge regression on your own history, backtested against simply assuming no change, and only shown where it beats that.",
            "A projection of a pattern, not a prediction of your life. It knows nothing about what you plan to do, and anything that breaks the pattern invalidates it."),

        new(MetabolicPattern, Grade.C,
            "Several metabolic measures drifting the same way at once says more than any one of them.",
            "The individual components are established, and reading them together is ordinary clinical practice.",
            "The COMBINATION as scored here is not a validated instrument. It is a reason to look at the panel and ask a doctor, and it is deliberately not a diagnosis of anything."),

        new(MetricKeys.Spo2Average, Grade.C,
            "Repeated overnight oxygen dips can point at disturbed breathing in sleep.",
            "Sleep-disordered breathing is well established, and consumer sensors do detect severe cases.",
            "Wrist and finger sensors are not clinical oximeters: skin tone, fit and movement all move the number. Normal readings here rule nothing out."),

        // -- D: experimental -----------------------------------------------------
        new(BiologicalAge, Grade.D,
            "An age-equivalent summarising several health measures.",
            "The idea is widely used and easy to read.",
            "There is no agreed definition, no agreed input set and no validated mapping to outcomes. Two biological-age tools disagree by years on the same person. It is a restatement of its own inputs in friendlier units, and it never overrides them."),

        new(MetricKeys.CardiovascularAge, Grade.D,
            "A vendor estimate of circulatory age against your years.",
            "None published. The method is not disclosed.",
            "An undisclosed estimate from a device maker. Shown because it is on the ring, graded honestly because nobody outside the company can check it."),

        new(MetricKeys.ReadinessScore, Grade.D,
            "A single daily number summarising how recovered you are.",
            "Built from inputs that are individually meaningful.",
            "The weighting is the vendor's, and is neither published nor validated. The components beneath it say more than the score does."),

        new(MetricKeys.SleepScore, Grade.D,
            "A single nightly number summarising sleep.",
            "Built from duration, timing and stages, which individually matter.",
            "An undisclosed weighting of real inputs. Sleep is multidimensional, and collapsing it loses the part worth acting on."),

        // -- The rest of the panel -----------------------------------------------
        //
        // Graded too, because an ungraded row in a graded table reads as an omission
        // rather than as a measurement nobody argues about.
        new(MetricKeys.TotalCholesterol, Grade.B,
            "Total cholesterol is part of the standard lipid panel and feeds the validated risk equations.",
            "Long-standing use, and it is an input to the risk models rather than a conclusion.",
            "On its own it says very little: a high total driven by high HDL and a high total driven by high LDL are different situations with the same number."),

        new(MetricKeys.Tsh, Grade.A,
            "TSH is the first-line test of thyroid function.",
            "Standard practice; the pituitary signal moves before the thyroid hormones leave their range.",
            "The upper bound of the printed range is contested, and TSH is suppressed by acute illness, pregnancy and several drugs. One abnormal value is repeated before it means anything."),

        new(MetricKeys.VitaminD, Grade.C,
            "Low vitamin D is associated with a long list of poor outcomes.",
            "The deficiency state and its effect on bone are established and not in doubt.",
            "Beyond bone health, the association is mostly not causal: large supplementation trials have largely failed to show the benefits the observational data implied. Low vitamin D looks more like a marker of poor health than a cause of it."),

        new(MetricKeys.Creatinine, Grade.A,
            "Serum creatinine is the standard input to kidney function estimation.",
            "Universal clinical use.",
            "Not read on its own here: it is produced by muscle, so it is interpreted through eGFR, which accounts for age and sex."),

        new(MetricKeys.Alt, Grade.A,
            "ALT is the standard first-line test of liver injury.",
            "Universal clinical use.",
            "Mild elevations are common and most often fatty liver, which travels with the metabolic markers. The printed upper limits are argued to be too high to catch it."),

        new(MetricKeys.Ast, Grade.A,
            "AST is part of the standard liver panel.",
            "Universal clinical use, and the ratio to ALT carries information.",
            "Also found in skeletal muscle and in red cells, so hard exercise shortly before the draw raises it with no liver involvement at all."),

        new(MetricKeys.Ferritin, Grade.A,
            "Ferritin is the best single measure of iron stores.",
            "Established; low ferritin is the earliest stage of iron depletion.",
            "An acute-phase reactant, so inflammation raises it. A normal ferritin alongside a raised CRP does not rule out low iron."),

        new(MetricKeys.VitaminB12, Grade.A,
            "B12 deficiency causes anaemia and neurological damage, and is correctable.",
            "Established, and the harm from missing it is well documented.",
            "The lower end of the printed range is contested, and serum B12 is an imperfect measure of what is inside cells."),

        new(MetricKeys.Folate, Grade.A,
            "Folate deficiency causes anaemia and is correctable.",
            "Established.",
            "Read with B12: correcting folate alone in someone B12-deficient fixes the blood count while nerve damage continues."),

        new(MetricKeys.BodyFatPct, Grade.B,
            "Fat mass, rather than weight, is what carries metabolic risk.",
            "The underlying relationship is well established.",
            "Depends entirely on how it was measured. Bioimpedance scales move several percent with hydration and time of day, and are far better at tracking your own change than at stating a true value."),

        new(MetricKeys.LeanMassKg, Grade.B,
            "Preserving lean mass protects function, metabolic health and independence with age.",
            "Strong and consistent, particularly for strength and physical function in later life.",
            "Same measurement caveat as body fat, since most scales derive one from the other. Only the direction over months is worth reading."),

        // -- The method the sparse tier rests on ---------------------------------
        new(LabReferenceRange, Grade.A,
            "A measured value read against the interval a laboratory prints.",
            "The standard way blood results are reported everywhere.",
            "A population interval, usually the middle 95% of a reference group -- so roughly one healthy person in twenty falls outside one by definition. It is not a treatment threshold and not a personal target."),
    ];

    private static readonly Dictionary<string, EvidenceNote> ByKey =
        All.ToDictionary(n => n.Key, StringComparer.OrdinalIgnoreCase);

    public static EvidenceNote? For(string key) => ByKey.GetValueOrDefault(key);

    public static Grade? GradeFor(string key) => ByKey.TryGetValue(key, out var n) ? n.Grade : null;

    // The business rule, in one place: nothing emerging or experimental may be stated
    // without the sentence that says so.
    public static bool MustQualify(string key) => ByKey.TryGetValue(key, out var n) && n.MustQualify;
}
