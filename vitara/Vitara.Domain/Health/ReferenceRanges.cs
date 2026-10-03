using Vitara.Domain.Entities;

namespace Vitara.Domain.Health;

// What a laboratory calls normal, for the analytes this system understands.
//
// Three things this is NOT, all of them easy to assume and each one a different way of
// being wrong about somebody's blood:
//
//   1. Not a diagnosis, and not a threshold for action. A value outside a reference
//      range is a reason to ask a doctor a question. Several of these ranges are known
//      to be contested -- the "optimal" LDL for someone with a family history is far
//      below the range any lab prints -- and the system takes no position on that.
//   2. Not the user's own lab's ranges. Reporting conventions genuinely differ between
//      labs and between assays, which is why ReferenceRange carries a LabName and why
//      these seeds leave it null. A range from the user's own report should replace the
//      seed, and the row is editable for exactly that reason.
//   3. Not a baseline. A baseline is what is normal FOR THIS PERSON and is earned from
//      their own readings; a reference range is what is normal for adults in general.
//      Labs arrive twice a year, which is far too sparse to learn a personal range
//      from, so these are the one place in Vitara where a population number is used at
//      all -- and they are labelled as such everywhere they are shown.
//
// Adult, non-pregnant, conventional US units, which is what the rest of the catalogue
// uses. Where a range differs by sex the rows differ by sex; where it does not, Sex is
// null and the row applies to everyone.
public static class ReferenceRanges
{
    // Source is recorded per row rather than in one blanket sentence, because "a lab
    // prints this" and "a guideline body recommends this" are different kinds of claim
    // and the second one is the kind that changes.
    public static IReadOnlyList<ReferenceRange> Seed =>
    [
        new()
        {
            Metric = MetricKeys.Hba1c, Low = 4.0, High = 5.6, Unit = "%",
            Notes = "Typical non-diabetic adult range. 5.7-6.4% is commonly reported as prediabetes and 6.5%+ as diabetes.",
        },
        new()
        {
            Metric = MetricKeys.TotalCholesterol, High = 200, Unit = "mg/dL",
            Notes = "Commonly reported as desirable below 200. A total on its own says little without the fractions below it.",
        },
        new()
        {
            Metric = MetricKeys.Ldl, High = 100, Unit = "mg/dL",
            Notes = "Commonly reported as optimal below 100. Guideline targets are lower for people at higher cardiovascular risk, which this does not know about you.",
        },
        new()
        {
            Metric = MetricKeys.Hdl, Low = 40, Unit = "mg/dL", Sex = "male",
            Notes = "Below 40 mg/dL is commonly flagged as low for men. Higher is generally considered better.",
        },
        new()
        {
            Metric = MetricKeys.Hdl, Low = 50, Unit = "mg/dL", Sex = "female",
            Notes = "Below 50 mg/dL is commonly flagged as low for women. Higher is generally considered better.",
        },
        new()
        {
            Metric = MetricKeys.Triglycerides, High = 150, Unit = "mg/dL",
            Notes = "Commonly reported as normal below 150. Measured fasting; a non-fasting draw reads higher.",
        },
        new()
        {
            Metric = MetricKeys.Crp, High = 3.0, Unit = "mg/L",
            Notes = "High-sensitivity CRP. Below 1 is commonly described as lower cardiovascular risk and above 3 as higher. A recent infection raises it for weeks and makes a single reading uninformative.",
        },
        new()
        {
            Metric = MetricKeys.Tsh, Low = 0.4, High = 4.0, Unit = "mIU/L",
            Notes = "Adult range. The upper bound is contested; some clinicians work to 2.5 in people with symptoms.",
        },
        new()
        {
            Metric = MetricKeys.VitaminD, Low = 30, High = 100, Unit = "ng/mL",
            Notes = "25-hydroxyvitamin D. Below 20 is commonly called deficient and 20-29 insufficient.",
        },

        new()
        {
            Metric = MetricKeys.FastingInsulin, Low = 2.6, High = 24.9, Unit = "µIU/mL",
            Notes = "A typical laboratory interval, and an unusually wide one: assays are not standardised between labs, so a value should be read against the range on your own report. Many clinicians working on metabolic health treat anything above about 10 as worth discussing, well inside the printed range.",
        },
        new()
        {
            Metric = MetricKeys.HomaIr, High = 2.0, Unit = "index",
            Notes = "Commonly described as insulin-sensitive below about 2. There is no agreed cut-off and it varies by population and by assay; the trend across draws is the usable part.",
        },
        new()
        {
            Metric = MetricKeys.ApoB, High = 90, Unit = "mg/dL",
            Notes = "Commonly reported as desirable below about 90, and below 80 or lower for people at higher cardiovascular risk. Which applies to you depends on risk this system does not know.",
        },
        new()
        {
            Metric = MetricKeys.Lpa, High = 75, Unit = "nmol/L",
            Notes = "Commonly reported as raised above about 75 nmol/L (roughly 30 mg/dL). Units differ between labs and do not convert reliably, so check which your report used.",
        },
        new()
        {
            Metric = MetricKeys.NonHdl, High = 130, Unit = "mg/dL",
            Notes = "Commonly reported as desirable below 130, which is conventionally 30 above the LDL target it accompanies.",
        },
        new()
        {
            Metric = MetricKeys.Egfr, Low = 90, Unit = "mL/min/1.73m²",
            Notes = "90 and above is conventionally normal, 60-89 mildly reduced, and below 60 sustained for three months defines chronic kidney disease. It falls slowly with age in people with healthy kidneys.",
        },
        new()
        {
            Metric = MetricKeys.Creatinine, Low = 0.74, High = 1.35, Unit = "mg/dL", Sex = "male",
            Notes = "Depends heavily on muscle mass. Read through eGFR rather than on its own.",
        },
        new()
        {
            Metric = MetricKeys.Creatinine, Low = 0.59, High = 1.04, Unit = "mg/dL", Sex = "female",
            Notes = "Depends heavily on muscle mass. Read through eGFR rather than on its own.",
        },
        new()
        {
            Metric = MetricKeys.Alt, High = 33, Unit = "U/L", Sex = "female",
            Notes = "Upper limits differ by sex and between laboratories, and several bodies argue the commonly printed limits are too high to catch fatty liver.",
        },
        new()
        {
            Metric = MetricKeys.Alt, High = 40, Unit = "U/L", Sex = "male",
            Notes = "Upper limits differ by sex and between laboratories, and several bodies argue the commonly printed limits are too high to catch fatty liver.",
        },
        new()
        {
            Metric = MetricKeys.Ast, Low = 10, High = 40, Unit = "U/L",
            Notes = "Also found in muscle: hard exercise in the day or two before a draw raises it without anything being wrong with the liver.",
        },
        new()
        {
            Metric = MetricKeys.Ferritin, Low = 30, High = 400, Unit = "ng/mL", Sex = "male",
            Notes = "Rises with any inflammation, so a normal ferritin alongside a raised CRP does not rule out low iron stores.",
        },
        new()
        {
            Metric = MetricKeys.Ferritin, Low = 15, High = 200, Unit = "ng/mL", Sex = "female",
            Notes = "Rises with any inflammation, so a normal ferritin alongside a raised CRP does not rule out low iron stores. Many clinicians treat below 30 as low where there are symptoms.",
        },
        new()
        {
            Metric = MetricKeys.VitaminB12, Low = 200, High = 900, Unit = "pg/mL",
            Notes = "The lower end of the printed range is contested; symptoms occur in some people in the 200-400 band, where methylmalonic acid is the better test.",
        },
        new()
        {
            Metric = MetricKeys.Folate, Low = 3.0, Unit = "ng/mL",
            Notes = "Read alongside B12. Treating folate while B12 is low is a recognised way to correct the blood count while nerve damage continues.",
        },

        // The medium tier has published ranges too, and a glucose reading taken at home
        // deserves the same treatment as one drawn at a lab.
        new()
        {
            Metric = MetricKeys.Glucose, Low = 70, High = 99, Unit = "mg/dL",
            Notes = "Fasting. 100-125 is commonly reported as prediabetes and 126+ as diabetes, each on repeat testing.",
        },
        new()
        {
            Metric = MetricKeys.SystolicBp, High = 120, Unit = "mmHg",
            Notes = "Commonly reported as normal below 120. A single reading decides nothing; the average of several, taken properly, is the measurement.",
        },
        new()
        {
            Metric = MetricKeys.DiastolicBp, High = 80, Unit = "mmHg",
            Notes = "Commonly reported as normal below 80.",
        },
    ];

    // The range that applies to this person, or none.
    //
    // Most specific wins: a row naming their lab beats a generic one, and a row naming
    // their sex beats one that applies to everyone. Falling back to a generic range is
    // right -- most analytes have no sex-specific range -- but silently applying a male
    // range to a woman because it sorted first would not be.
    public static ReferenceRange? For(
        IEnumerable<ReferenceRange> ranges, string metric, string? sex = null, int? age = null, string? labName = null)
    {
        var candidates = ranges.Where(r =>
            r.Metric == metric
            && (r.Sex is null || string.Equals(r.Sex, sex, StringComparison.OrdinalIgnoreCase))
            && (r.LabName is null || string.Equals(r.LabName, labName, StringComparison.OrdinalIgnoreCase))
            && (r.AgeMin is null || age is null || age >= r.AgeMin)
            && (r.AgeMax is null || age is null || age <= r.AgeMax));

        return candidates
            .OrderByDescending(r => r.LabName is not null ? 1 : 0)
            .ThenByDescending(r => r.Sex is not null ? 1 : 0)
            .ThenByDescending(r => r.AgeMin is not null || r.AgeMax is not null ? 1 : 0)
            .FirstOrDefault();
    }

    public enum Standing { Unknown, Below, Within, Above }

    public static Standing Where(double value, ReferenceRange? range)
    {
        if (range is null) return Standing.Unknown;
        if (range.Low is { } low && value < low) return Standing.Below;
        if (range.High is { } high && value > high) return Standing.Above;
        return Standing.Within;
    }

    // Said the way a person would say it, and never as a verdict. "Above the range"
    // is a fact about an interval; "high" is a judgement about a body.
    public static string Describe(Standing standing, ReferenceRange? range) => standing switch
    {
        Standing.Below => range?.Low is { } low ? $"below the usual range, which starts at {Trim(low)}" : "below the usual range",
        Standing.Above => range?.High is { } high ? $"above the usual range, which ends at {Trim(high)}" : "above the usual range",
        Standing.Within => range is null ? "" : $"inside the usual range, {Band(range)}",
        _ => "no reference range recorded for this one",
    };

    public static string Band(ReferenceRange r) =>
        (r.Low, r.High) switch
        {
            ({ } low, { } high) => $"{Trim(low)}-{Trim(high)} {r.Unit}",
            (null, { } high) => $"under {Trim(high)} {r.Unit}",
            ({ } low, null) => $"over {Trim(low)} {r.Unit}",
            _ => r.Unit,
        };

    private static string Trim(double v) => v == Math.Floor(v) ? ((long)v).ToString() : v.ToString("0.#");
}
