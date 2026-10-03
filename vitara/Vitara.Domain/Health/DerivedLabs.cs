namespace Vitara.Domain.Health;

// One value worked out from a draw rather than measured in it.
public record DerivedValue(
    string Metric,
    double Value,
    string Unit,
    Grade Grade,
    string Method,                      // the arithmetic, stated
    IReadOnlyList<string> From,         // the analytes it used
    string Caveat);

// Why something was not worked out.
//
// AS IMPORTANT AS THE VALUES, and the reason this returns a record rather than just
// skipping. "No HOMA-IR" and "you have fasting glucose but nobody ordered fasting
// insulin, so insulin resistance cannot be seen in this panel" are the same absence
// and completely different information: the first looks like the feature does not
// exist, the second is a sentence to take to the next appointment.
public record DerivedRefusal(string Metric, string Label, string Reason, IReadOnlyList<string> Missing);

public record DerivedSet(IReadOnlyList<DerivedValue> Values, IReadOnlyList<DerivedRefusal> Refusals);

// What can be read out of one blood draw that nobody measured directly.
//
// All three of these are arithmetic over analytes that are already in the panel, and
// all three are routinely absent from the report the lab hands back -- non-HDL because
// it takes a subtraction nobody does, HOMA-IR because fasting insulin is rarely
// ordered, eGFR because some labs print it and some do not.
//
// THE DESIGN RULE IS THAT EVERY ONE OF THEM REFUSES RATHER THAN SUBSTITUTES. The
// tempting failures are specific and each one produces a plausible wrong number:
//
//   - Computing HOMA-IR from a non-fasting glucose. The arithmetic works, the output
//     looks like an insulin-resistance score, and it is meaningless -- post-meal
//     insulin is several times fasting insulin and the index is defined only at rest.
//   - Pairing analytes from different draws. Glucose from March and insulin from
//     September is a number describing nobody.
//   - Falling back to a default sex or a default age in the kidney equation. Age moves
//     eGFR by tens of units; assuming it is not an estimate, it is a fabrication.
//
// Pure, takes one draw, takes the clock from nowhere. Values in conventional US units
// to match the rest of the catalogue.
public static class DerivedLabs
{
    // Inputs outside these bounds are refused rather than computed. Not reference
    // ranges -- these are the bounds of a value that could have come from a human at
    // all, and a transcription error is the overwhelmingly likely explanation outside
    // them. Computing on a mistyped value quietly produces a finding about a typo.
    private static readonly Dictionary<string, (double Min, double Max)> Plausible = new()
    {
        [MetricKeys.Glucose] = (20, 800),
        [MetricKeys.FastingInsulin] = (0.1, 400),
        [MetricKeys.TotalCholesterol] = (50, 800),
        [MetricKeys.Hdl] = (5, 200),
        [MetricKeys.Creatinine] = (0.1, 20),
    };

    public static DerivedSet From(
        IReadOnlyDictionary<string, double> panel,
        bool? fasting = null,
        string? sex = null,
        int? age = null)
    {
        var values = new List<DerivedValue>();
        var refusals = new List<DerivedRefusal>();

        NonHdl(panel, values, refusals);
        HomaIr(panel, fasting, values, refusals);
        Egfr(panel, sex, age, values, refusals);

        return new DerivedSet(values, refusals);
    }

    // ── Non-HDL cholesterol ─────────────────────────────────────────────────────
    //
    // Total minus HDL: everything carried in a particle that can lodge in an artery
    // wall. One subtraction, needs no extra blood and no fasting, and most reports
    // leave it to the reader.
    private static void NonHdl(
        IReadOnlyDictionary<string, double> panel, List<DerivedValue> values, List<DerivedRefusal> refusals)
    {
        const string label = "Non-HDL cholesterol";

        var missing = Missing(panel, MetricKeys.TotalCholesterol, MetricKeys.Hdl);
        if (missing.Count > 0)
        {
            refusals.Add(new DerivedRefusal(MetricKeys.NonHdl, label,
                "Needs total cholesterol and HDL from the same draw.", missing));
            return;
        }

        var total = panel[MetricKeys.TotalCholesterol];
        var hdl = panel[MetricKeys.Hdl];

        if (!Within(MetricKeys.TotalCholesterol, total) || !Within(MetricKeys.Hdl, hdl))
        {
            refusals.Add(new DerivedRefusal(MetricKeys.NonHdl, label,
                "One of the values is outside the range a result could plausibly take, so this looks like a typing error rather than a reading.", []));
            return;
        }

        // HDL is a fraction of the total, so this cannot be negative on real data.
        // When it is, the two numbers did not come from the same panel or one of them
        // is in different units.
        if (hdl >= total)
        {
            refusals.Add(new DerivedRefusal(MetricKeys.NonHdl, label,
                "HDL is not lower than total cholesterol, which cannot happen within one panel. Check the two values and their units.", []));
            return;
        }

        values.Add(new DerivedValue(
            MetricKeys.NonHdl,
            Math.Round(total - hdl, 1),
            "mg/dL",
            Grade.A,
            $"Total cholesterol {Trim(total)} minus HDL {Trim(hdl)}.",
            [MetricKeys.TotalCholesterol, MetricKeys.Hdl],
            "Arithmetic from the panel, so it carries any error in the two values behind it."));
    }

    // ── HOMA-IR ─────────────────────────────────────────────────────────────────
    //
    // (fasting glucose mg/dL x fasting insulin uIU/mL) / 405.
    //
    // The one the longevity literature cares most about and the one most likely to be
    // computed wrongly, because the inputs look available far more often than they
    // actually are. Insulin resistance builds for years while fasting glucose stays
    // flat, so a panel with glucose and no insulin cannot see the thing that is
    // happening -- and saying that out loud is more useful than any value would be.
    private static void HomaIr(
        IReadOnlyDictionary<string, double> panel, bool? fasting,
        List<DerivedValue> values, List<DerivedRefusal> refusals)
    {
        const string label = "HOMA-IR";

        var missing = Missing(panel, MetricKeys.Glucose, MetricKeys.FastingInsulin);
        if (missing.Count > 0)
        {
            var reason = missing.Contains(MetricKeys.FastingInsulin) && !missing.Contains(MetricKeys.Glucose)
                ? "This panel has fasting glucose but no fasting insulin. Insulin rises for years before glucose does, so insulin resistance cannot be seen from glucose alone — fasting insulin is the test to ask for."
                : "Needs fasting glucose and fasting insulin from the same draw.";

            refusals.Add(new DerivedRefusal(MetricKeys.HomaIr, label, reason, missing));
            return;
        }

        // The index is defined at rest. After a meal, insulin is several times its
        // fasting level and the arithmetic still produces a number -- a large, alarming
        // and meaningless one. An unrecorded fasting state is refused as firmly as a
        // known-fed one, because "probably fasting" is how a wrong number gets shipped.
        if (fasting is not true)
        {
            refusals.Add(new DerivedRefusal(MetricKeys.HomaIr, label,
                fasting is false
                    ? "This draw was not fasting. HOMA-IR is only defined on a fasting sample; after food, insulin is several times higher and the result would be meaningless."
                    : "This draw does not record whether it was fasting. HOMA-IR is only defined on a fasting sample, so it is left uncomputed rather than assumed.",
                []));
            return;
        }

        var glucose = panel[MetricKeys.Glucose];
        var insulin = panel[MetricKeys.FastingInsulin];

        if (!Within(MetricKeys.Glucose, glucose) || !Within(MetricKeys.FastingInsulin, insulin))
        {
            refusals.Add(new DerivedRefusal(MetricKeys.HomaIr, label,
                "One of the values is outside the range a result could plausibly take, so this looks like a typing error rather than a reading.", []));
            return;
        }

        values.Add(new DerivedValue(
            MetricKeys.HomaIr,
            Math.Round(glucose * insulin / 405.0, 2),
            "index",
            Grade.B,
            $"Fasting glucose {Trim(glucose)} mg/dL times fasting insulin {Trim(insulin)} µIU/mL, divided by 405.",
            [MetricKeys.Glucose, MetricKeys.FastingInsulin],
            "Insulin assays are not standardised between laboratories, so this does not transfer between labs and there is no universal cut-off. The direction across several draws is the usable part. It is not interpretable on insulin therapy."));
    }

    // ── eGFR, CKD-EPI 2021 ──────────────────────────────────────────────────────
    //
    // The current recommended equation, and deliberately the race-free 2021 revision:
    // the earlier version carried a coefficient that raised the estimate for Black
    // patients, which is now withdrawn as having no biological basis and as having
    // delayed referrals. Nothing here asks about race and nothing should.
    private static void Egfr(
        IReadOnlyDictionary<string, double> panel, string? sex, int? age,
        List<DerivedValue> values, List<DerivedRefusal> refusals)
    {
        const string label = "Kidney function (eGFR)";

        var missing = Missing(panel, MetricKeys.Creatinine);
        var female = Female(sex);

        var absent = new List<string>(missing);
        if (age is null) absent.Add("age");
        if (female is null) absent.Add("sex");

        if (absent.Count > 0 || age is not { } years || female is not { } isFemale)
        {
            refusals.Add(new DerivedRefusal(MetricKeys.Egfr, label,
                missing.Count > 0 && age is not null && female is not null
                    ? "Needs creatinine from the draw."
                    : "Needs creatinine, plus age and sex from your profile. Age alone moves this estimate by tens of units, so it is not computed with a default.",
                absent));
            return;
        }

        var scr = panel[MetricKeys.Creatinine];
        if (!Within(MetricKeys.Creatinine, scr))
        {
            refusals.Add(new DerivedRefusal(MetricKeys.Egfr, label,
                "The creatinine value is outside the range a result could plausibly take, so this looks like a typing error rather than a reading.", []));
            return;
        }

        var kappa = isFemale ? 0.7 : 0.9;
        var alpha = isFemale ? -0.241 : -0.302;
        var ratio = scr / kappa;

        var egfr = 142.0
                   * Math.Pow(Math.Min(ratio, 1.0), alpha)
                   * Math.Pow(Math.Max(ratio, 1.0), -1.200)
                   * Math.Pow(0.9938, years)
                   * (isFemale ? 1.012 : 1.0);

        values.Add(new DerivedValue(
            MetricKeys.Egfr,
            Math.Round(egfr, 0),
            "mL/min/1.73m²",
            Grade.A,
            $"CKD-EPI 2021 from creatinine {Trim(scr)} mg/dL at age {years}.",
            [MetricKeys.Creatinine],
            "Creatinine comes from muscle, so this reads low in someone very muscular and high in someone with little muscle, and neither is a kidney finding. A single result does not define kidney disease — that needs the same finding sustained over three months."));
    }

    // ── Shared ──────────────────────────────────────────────────────────────────

    private static List<string> Missing(IReadOnlyDictionary<string, double> panel, params string[] needed) =>
        needed.Where(n => !panel.ContainsKey(n)).ToList();

    private static bool Within(string metric, double value) =>
        !Plausible.TryGetValue(metric, out var b) || (value >= b.Min && value <= b.Max);

    // Null rather than a guess when the profile says something unexpected: the kidney
    // equation needs one of two coefficient sets and there is no third branch to fall
    // back on.
    private static bool? Female(string? sex) => sex?.Trim().ToLowerInvariant() switch
    {
        "female" or "f" or "woman" => true,
        "male" or "m" or "man" => false,
        _ => null,
    };

    private static string Trim(double v) => v == Math.Floor(v) ? ((long)v).ToString() : v.ToString("0.##");
}
