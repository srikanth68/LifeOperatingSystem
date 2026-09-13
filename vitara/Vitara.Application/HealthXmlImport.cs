using System.Globalization;
using System.Xml;
using Vitara.Domain.Health;

namespace Vitara.Application;

// One day's worth of one metric, from one app.
public record XmlDailyValue(DateOnly Day, string Metric, string Source, double Value, int Samples);

// What one app contributed, for the source picker.
public record XmlSourceSummary(string Source, int Records, int Days, List<string> Metrics)
{
    // Oura already reaches Vitara through its own API, with sleep stages, RMSSD and a
    // skin-temperature deviation that Apple Health never receives. Importing the Apple
    // copy on top would overwrite the better record with a coarser version of itself.
    public bool RecommendedOff => Source.Contains("oura", StringComparison.OrdinalIgnoreCase);
}

public record XmlScanResult(
    long BytesRead,
    int RecordsSeen,
    int RecordsMapped,
    DateOnly? FirstDay,
    DateOnly? LastDay,
    List<XmlSourceSummary> Sources,
    List<string> IgnoredTypes,
    List<string> Warnings,
    double? HeightMetres,
    List<XmlDailyValue> Daily);

// Reading Apple Health's own export.
//
// Not the same problem as the spreadsheet importer, and not solvable the same way. The
// real file measured 810MB with 1.7 million records, so nothing here may hold the
// document in memory -- XmlReader streams it, and the only thing that accumulates is
// one number per (day, metric, app).
//
// Three things in a real export that a naive reader gets wrong, each silently:
//
//   IMPERIAL UNITS. The file carries lb, in, mi and degF. A body mass of 176 stored as
//   kilograms is a plausible-looking number that poisons every baseline built on it.
//
//   SDNN IS NOT RMSSD. Apple records heart-rate variability as SDNN; Oura records
//   RMSSD. They are different statistics over the same intervals and are not
//   interchangeable -- pooling them produces a baseline describing neither. Apple's
//   lands on its own metric key.
//
//   SLEEP IS SPANS, NOT VALUES. A SleepAnalysis record has no number on it at all: it
//   is a start, an end, and a stage name. A night is the summed duration of the asleep
//   spans, and the night belongs to the day the user WOKE, which is what both Oura and
//   Apple mean by a sleep day.
public static class HealthXmlImport
{
    // Apple's own key for HRV, kept distinct from MetricKeys.HrvRmssd on purpose.
    public const string HrvSdnn = "hrv_sdnn";

    // Types worth storing, and how several readings in a day combine.
    //
    // Anything not listed is ignored and reported. That includes raw HeartRate --
    // 461,357 records in the real export -- which Vitara deliberately does not project:
    // it is the one series that would fill the disk on a box also hosting the model.
    private static readonly Dictionary<string, (string Metric, bool Cumulative)> Mapped = new()
    {
        ["HKQuantityTypeIdentifierStepCount"] = (MetricKeys.Steps, true),
        ["HKQuantityTypeIdentifierActiveEnergyBurned"] = (MetricKeys.ActiveCalories, true),
        ["HKQuantityTypeIdentifierRestingHeartRate"] = (MetricKeys.RestingHeartRate, false),
        ["HKQuantityTypeIdentifierHeartRateVariabilitySDNN"] = (HrvSdnn, false),
        ["HKQuantityTypeIdentifierRespiratoryRate"] = (MetricKeys.BreathingRate, false),
        ["HKQuantityTypeIdentifierOxygenSaturation"] = (MetricKeys.Spo2Average, false),
        ["HKQuantityTypeIdentifierBodyMass"] = (MetricKeys.WeightKg, false),
        ["HKQuantityTypeIdentifierVO2Max"] = (MetricKeys.Vo2Max, false),
        ["HKQuantityTypeIdentifierBloodPressureSystolic"] = (MetricKeys.SystolicBp, false),
        ["HKQuantityTypeIdentifierBloodPressureDiastolic"] = (MetricKeys.DiastolicBp, false),
        ["HKQuantityTypeIdentifierWaistCircumference"] = (MetricKeys.WaistCircumferenceCm, false),
    };

    private const string SleepType = "HKCategoryTypeIdentifierSleepAnalysis";
    private const string HeightType = "HKQuantityTypeIdentifierHeight";

    // Apple splits sleep into stages. Only the asleep ones count toward a total --
    // "InBed" is time in bed, and counting it as sleep is how a restless night becomes
    // a long one.
    private static bool IsAsleep(string stageValue) =>
        stageValue.Contains("Asleep", StringComparison.OrdinalIgnoreCase);

    // What a real reading can be.
    //
    // A real export contained a "resting heart rate" far above any resting value, a
    // night totalling under a minute and another over fifteen hours. None of those are
    // measurements; they are an app writing to the wrong field, a sensor glitch, and a
    // day with a stuck span.
    //
    // Left in, they do exactly what the Oura naps did: a baseline pooling a 0.5-minute
    // night with 400-minute ones has a collapsed mean and an exploded spread, and every
    // z-score against it is meaningless. Ten years of history makes that worse, not
    // better -- there is simply more of it.
    //
    // The bounds are deliberately generous. This is not a filter for unusual readings,
    // which are the interesting ones; it is a filter for values that cannot be a
    // measurement of this thing at all. Anything dropped is counted and reported.
    private static readonly Dictionary<string, (double Min, double Max)> Plausible = new()
    {
        // A resting heart rate above 110 is not resting. 176 is a sprint.
        [MetricKeys.RestingHeartRate] = (30, 110),
        [HrvSdnn] = (3, 300),
        [MetricKeys.BreathingRate] = (4, 40),
        [MetricKeys.Spo2Average] = (70, 100),

        // A day's summed sleep. Under an hour the device caught fragments rather than a
        // night; over fourteen hours a span is stuck open.
        [MetricKeys.TotalSleepMinutes] = (60, 840),

        // Under a hundred steps means the phone spent the day on a table, which is a
        // fact about the phone rather than about the user.
        [MetricKeys.Steps] = (100, 100_000),
        [MetricKeys.ActiveCalories] = (1, 10_000),

        [MetricKeys.WeightKg] = (25, 300),
        [MetricKeys.Vo2Max] = (10, 90),
        [MetricKeys.SystolicBp] = (60, 260),
        [MetricKeys.DiastolicBp] = (30, 160),
        [MetricKeys.WaistCircumferenceCm] = (40, 200),
    };

    internal static bool IsPlausible(string metric, double value) =>
        !Plausible.TryGetValue(metric, out var range) || (value >= range.Min && value <= range.Max);

    private sealed class Bucket
    {
        public double Sum;
        public int Count;
        public double Value(bool cumulative) => cumulative ? Sum : Sum / Math.Max(1, Count);
    }

    public static XmlScanResult Scan(Stream xml, long byteLength = 0)
    {
        var buckets = new Dictionary<(DateOnly Day, string Metric, string Source), Bucket>();
        var sourceRecords = new Dictionary<string, int>(StringComparer.Ordinal);
        var sourceMetrics = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var ignored = new Dictionary<string, int>(StringComparer.Ordinal);
        var warnings = new List<string>();

        var seen = 0;
        var mapped = 0;
        var badDates = 0;
        double? heightMetres = null;

        var settings = new XmlReaderSettings
        {
            // Apple's export declares a DTD. Resolving it is both pointless here and an
            // external-entity risk on a file the user did not write.
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            XmlResolver = null,
        };

        using var reader = XmlReader.Create(xml, settings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Name != "Record") continue;

            seen++;

            var type = reader.GetAttribute("type");
            if (string.IsNullOrEmpty(type)) continue;

            var source = reader.GetAttribute("sourceName") ?? "unknown";
            var unit = reader.GetAttribute("unit") ?? "";
            var rawValue = reader.GetAttribute("value") ?? "";

            sourceRecords[source] = sourceRecords.GetValueOrDefault(source) + 1;

            // Height fills a profile field rather than a time series -- one number that
            // does not change, and the field BMI and waist-to-height both need.
            if (type == HeightType)
            {
                if (TryNumber(rawValue, out var h)) heightMetres = ToMetres(h, unit);
                continue;
            }

            if (type == SleepType)
            {
                if (!IsAsleep(rawValue)) continue;

                var start = reader.GetAttribute("startDate");
                var end = reader.GetAttribute("endDate");

                if (!TryInstant(start, out var from) || !TryInstant(end, out var to)) { badDates++; continue; }

                var minutes = (to - from).TotalMinutes;
                if (minutes <= 0 || minutes > 24 * 60) continue;

                // The night belongs to the day the user woke. Using the start date files
                // everything that began before midnight under the previous day, which is
                // the opposite of what both Oura and Apple mean by a sleep day.
                Add(buckets, DateOnly.FromDateTime(to.DateTime), MetricKeys.TotalSleepMinutes, source, minutes);
                Track(sourceMetrics, source, MetricKeys.TotalSleepMinutes);
                mapped++;
                continue;
            }

            if (!Mapped.TryGetValue(type, out var spec))
            {
                ignored[type] = ignored.GetValueOrDefault(type) + 1;
                continue;
            }

            if (!TryNumber(rawValue, out var value)) continue;
            if (!TryInstant(reader.GetAttribute("startDate"), out var at)) { badDates++; continue; }

            Add(buckets, DateOnly.FromDateTime(at.DateTime), spec.Metric, source, Convert(spec.Metric, value, unit));
            Track(sourceMetrics, source, spec.Metric);
            mapped++;
        }

        if (badDates > 0) warnings.Add($"{badDates:N0} record(s) skipped — the date could not be read.");

        // Bounded AFTER aggregation, not per record. A single 30-second sleep span is
        // not implausible on its own -- it is implausible as a night, and a night is
        // what the day's total claims to be.
        var aggregated = buckets
            .Select(kv => new XmlDailyValue(
                kv.Key.Day, kv.Key.Metric, kv.Key.Source,
                Math.Round(kv.Value.Value(Cumulative(kv.Key.Metric)), 3),
                kv.Value.Count))
            .ToList();

        var daily = aggregated.Where(d => IsPlausible(d.Metric, d.Value)).ToList();

        foreach (var group in aggregated.Except(daily).GroupBy(d => d.Metric))
            warnings.Add(
                $"{group.Count()} day(s) of {group.Key.Replace('_', ' ')} dropped as implausible " +
                $"(outside {Plausible[group.Key].Min:0.#}-{Plausible[group.Key].Max:0.#}).");

        var sources = sourceRecords
            .Select(kv => new XmlSourceSummary(
                kv.Key,
                kv.Value,
                daily.Where(d => d.Source == kv.Key).Select(d => d.Day).Distinct().Count(),
                sourceMetrics.TryGetValue(kv.Key, out var m) ? m.OrderBy(x => x).ToList() : []))
            .OrderByDescending(s => s.Records)
            .ToList();

        // The big ones only. A real export ignores three dozen types and listing every
        // one buries the handful the user might care about.
        var ignoredTop = ignored
            .OrderByDescending(kv => kv.Value)
            .Take(15)
            .Select(kv => $"{Short(kv.Key)} ({kv.Value:N0})")
            .ToList();

        return new XmlScanResult(
            byteLength, seen, mapped,
            daily.Count == 0 ? null : daily.Min(d => d.Day),
            daily.Count == 0 ? null : daily.Max(d => d.Day),
            sources, ignoredTop, warnings, heightMetres, daily);
    }

    private static void Add(
        Dictionary<(DateOnly, string, string), Bucket> buckets,
        DateOnly day, string metric, string source, double value)
    {
        var key = (day, metric, source);
        if (!buckets.TryGetValue(key, out var b)) buckets[key] = b = new Bucket();
        b.Sum += value;
        b.Count++;
    }

    private static void Track(Dictionary<string, HashSet<string>> map, string source, string metric)
    {
        if (!map.TryGetValue(source, out var set)) map[source] = set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(metric);
    }

    private static bool Cumulative(string metric) =>
        metric is MetricKeys.Steps or MetricKeys.ActiveCalories or MetricKeys.TotalSleepMinutes;

    private static string Short(string type) =>
        type.Replace("HKQuantityTypeIdentifier", "").Replace("HKCategoryTypeIdentifier", "");

    // ── Units ───────────────────────────────────────────────────────────────────
    //
    // Read off the unit attribute, never guessed. This export is imperial throughout --
    // lb, in, mi, degF -- and a weight in pounds stored as kilograms is a number that
    // looks entirely reasonable while making every baseline built on it wrong.

    internal static double Convert(string metric, double value, string unit)
    {
        var u = unit.Trim().ToLowerInvariant();

        if (metric == MetricKeys.WeightKg)
            return u is "lb" or "lbs" or "pound" or "pounds" ? value / 2.20462 : value;

        // Apple stores oxygen saturation as a fraction with a "%" unit attached: 0.97
        // rather than 97. Storing the fraction would put every reading two orders of
        // magnitude below its baseline.
        if (metric == MetricKeys.Spo2Average && value > 0 && value <= 1) return value * 100;

        return value;
    }

    internal static double ToMetres(double value, string unit) => unit.Trim().ToLowerInvariant() switch
    {
        "in" or "inch" or "inches" => value * 0.0254,
        "ft" or "feet" => value * 0.3048,
        "cm" => value / 100,
        _ => value,
    };

    // ── Parsing ─────────────────────────────────────────────────────────────────

    internal static bool TryNumber(string raw, out double value) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // Apple writes "2026-09-01 08:00:00 -0400". The offset is kept rather than
    // normalised to UTC: the DATE that matters is the local one the phone recorded, and
    // converting first files a late-evening reading under the following day.
    internal static bool TryInstant(string? raw, out DateTimeOffset at)
    {
        at = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        return DateTimeOffset.TryParseExact(
                   raw, "yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out at)
            || DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out at);
    }
}
