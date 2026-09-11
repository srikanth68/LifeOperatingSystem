using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Vitara.Domain.Health;

namespace Vitara.Application;

// One row of an imported sheet, reduced to what matters: a day, a metric, a number.
public record ImportedReading(DateOnly Day, string Metric, double Value, string SourceColumn);

// A column the importer recognised, and how many rows it actually got out of it.
public record MappedColumn(string Column, string Metric, int Rows);

// What an upload contains, before anything is written.
//
// Returned from a dry run and shown to the user first, deliberately. Apple Health
// exporters disagree about almost everything -- column names, date formats, whether
// each row is a day or a single sample -- so the importer guesses, and a guess about
// health data should be visible before it lands in the database rather than after.
//
// Ignored columns are listed rather than dropped silently. A sheet with a column the
// importer does not understand is the normal case, and the user is the only one who
// can tell whether the one it skipped was the one they cared about.
public record ImportPreview(
    string Shape,
    int RowsRead,
    DateOnly? FirstDay,
    DateOnly? LastDay,
    List<MappedColumn> Recognised,
    List<string> Ignored,
    List<string> Warnings,
    List<ImportedReading> Readings);

// Reading an Apple Health export out of a spreadsheet.
//
// Apple's own export is XML inside a zip, which nobody wants to hand-upload. What
// people actually have is a CSV or xlsx from one of the export apps, and those come in
// two shapes with nothing in the file declaring which:
//
//   WIDE   one row per day, one column per metric
//          Date,Steps,Resting Heart Rate,Sleep Analysis [Asleep]
//          2026-09-01,8431,54,7.4
//
//   LONG   one row per sample
//          type,startDate,value,unit
//          HKQuantityTypeIdentifierStepCount,2026-09-01,8431,count
//
// Both are detected rather than configured. A format dropdown is a question the user
// should not have to answer about a file they did not write.
public static class HealthImport
{
    // 50k rows is a couple of years of daily exports, or a few weeks of raw samples.
    // Beyond that this is the wrong tool -- and an unbounded parse on a 16GB box that
    // is also hosting the model is how a convenience feature takes the stack down.
    public const int MaxRows = 50_000;

    private static readonly string[] DateHeaders =
        ["date", "day", "startdate", "start", "start date", "creationdate", "datetime", "timestamp"];

    private static readonly string[] TypeHeaders = ["type", "metric", "name", "identifier"];
    private static readonly string[] ValueHeaders = ["value", "amount", "qty", "quantity"];

    // Header text to canonical metric.
    //
    // Matched on a normalised form -- lowercase, non-letters stripped -- so
    // "Resting Heart Rate", "resting_heart_rate", "RestingHeartRate" and
    // "HKQuantityTypeIdentifierRestingHeartRate" all land on the same key without a
    // synonym entry each.
    private static readonly Dictionary<string, string> Metrics = Build(new()
    {
        [MetricKeys.Steps] = ["steps", "stepcount", "stepstotal"],
        [MetricKeys.ActiveCalories] = ["activeenergy", "activeenergyburned", "activecalories", "activeenergykcal"],
        [MetricKeys.RestingHeartRate] = ["restingheartrate", "restinghr", "heartrateresting"],
        [MetricKeys.HrvRmssd] = ["heartratevariability", "hrv", "heartratevariabilitysdnn", "hrvsdnn", "hrvrmssd"],
        [MetricKeys.TotalSleepMinutes] = ["sleepanalysis", "sleepanalysisasleep", "asleep", "totalsleep", "sleepduration", "timeasleep"],
        [MetricKeys.WeightKg] = ["bodymass", "weight", "bodyweight"],
        [MetricKeys.SystolicBp] = ["bloodpressuresystolic", "systolic", "systolicbloodpressure"],
        [MetricKeys.DiastolicBp] = ["bloodpressurediastolic", "diastolic", "diastolicbloodpressure"],
        [MetricKeys.Glucose] = ["bloodglucose", "glucose", "bloodsugar"],
        [MetricKeys.Spo2Average] = ["oxygensaturation", "spo2", "bloodoxygen", "oxygensaturationpercent"],
        [MetricKeys.BreathingRate] = ["respiratoryrate", "breathingrate", "respirationrate"],
        [MetricKeys.Vo2Max] = ["vo2max", "vo2maximum"],
        [MetricKeys.Pulse] = ["heartrate", "hr", "heartratebpm"],
    });

    private static Dictionary<string, string> Build(Dictionary<string, string[]> src)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (metric, aliases) in src)
            foreach (var a in aliases)
                map[a] = metric;
        return map;
    }

    // Lowercase, letters and digits only. Kills spaces, underscores, brackets, units in
    // parentheses and Apple's "HKQuantityTypeIdentifier" prefix in one pass.
    internal static string Normalise(string header)
    {
        var sb = new StringBuilder(header.Length);
        foreach (var c in header)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));

        var s = sb.ToString();
        foreach (var prefix in new[] { "hkquantitytypeidentifier", "hkcategorytypeidentifier", "hkdatatype" })
            if (s.StartsWith(prefix, StringComparison.Ordinal)) return s[prefix.Length..];

        return s;
    }

    internal static string? MatchMetric(string header)
    {
        var n = Normalise(header);
        if (n.Length == 0) return null;
        if (Metrics.TryGetValue(n, out var exact)) return exact;

        // A header like "Resting Heart Rate (bpm)" normalises with the unit still
        // attached. Longest alias first so "heartratevariability" is not shadowed by
        // "heartrate".
        foreach (var alias in Metrics.Keys.OrderByDescending(k => k.Length))
            if (n.StartsWith(alias, StringComparison.Ordinal)) return Metrics[alias];

        return null;
    }

    // ── Entry point ─────────────────────────────────────────────────────────────

    public static ImportPreview Parse(Stream file, string fileName)
    {
        var table = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                 || fileName.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase)
            ? ReadXlsx(file)
            : ReadDelimited(file);

        if (table.Count == 0)
            return Empty("empty", ["The file has no rows."]);

        var header = table[0];
        var rows = table.Skip(1).ToList();

        var dateCol = IndexOf(header, DateHeaders);
        if (dateCol < 0)
            return Empty("unrecognised",
                [$"No date column found. Looked for: {string.Join(", ", DateHeaders)}. " +
                 $"The first row was: {string.Join(" | ", header.Take(12))}"]);

        var typeCol = IndexOf(header, TypeHeaders);
        var valueCol = IndexOf(header, ValueHeaders);

        // A type column AND a value column means one row per sample. Either alone is
        // ambiguous, and guessing wrong turns a metric name into a reading.
        return typeCol >= 0 && valueCol >= 0
            ? ParseLong(header, rows, dateCol, typeCol, valueCol)
            : ParseWide(header, rows, dateCol);
    }

    private static ImportPreview Empty(string shape, List<string> warnings) =>
        new(shape, 0, null, null, [], [], warnings, []);

    private static int IndexOf(List<string> header, string[] candidates)
    {
        for (var i = 0; i < header.Count; i++)
        {
            var n = Normalise(header[i]);
            if (candidates.Any(c => n == Normalise(c))) return i;
        }
        return -1;
    }

    // ── Wide: one row per day, one column per metric ─────────────────────────────

    private static ImportPreview ParseWide(List<string> header, List<List<string>> rows, int dateCol)
    {
        var mapped = new Dictionary<int, string>();
        var ignored = new List<string>();

        for (var i = 0; i < header.Count; i++)
        {
            if (i == dateCol) continue;
            var metric = MatchMetric(header[i]);
            if (metric is null)
            {
                if (!string.IsNullOrWhiteSpace(header[i])) ignored.Add(header[i]);
            }
            else if (mapped.ContainsValue(metric))
            {
                // Two columns claiming the same metric -- "Heart Rate [Min]" and
                // "Heart Rate [Max]" both reduce to pulse. Taking the first and saying
                // so beats averaging two different quantities together.
                ignored.Add($"{header[i]} (duplicate of {metric})");
            }
            else
            {
                mapped[i] = metric;
            }
        }

        var readings = new List<ImportedReading>();
        var warnings = new List<string>();
        var badDates = 0;

        foreach (var row in rows)
        {
            if (row.Count <= dateCol) continue;
            if (!TryDate(row[dateCol], out var day)) { badDates++; continue; }

            foreach (var (col, metric) in mapped)
            {
                if (row.Count <= col) continue;
                if (!TryValue(row[col], out var v)) continue;
                readings.Add(new ImportedReading(day, metric, Convert(metric, v, header[col]), header[col]));
            }
        }

        if (badDates > 0) warnings.Add($"{badDates} row(s) skipped — the date could not be read.");

        return Build(readings, "wide", rows.Count,
            mapped.Select(m => new MappedColumn(header[m.Key], m.Value, 0)).ToList(),
            ignored, warnings);
    }

    // ── Long: one row per sample ─────────────────────────────────────────────────

    private static ImportPreview ParseLong(
        List<string> header, List<List<string>> rows, int dateCol, int typeCol, int valueCol)
    {
        var readings = new List<ImportedReading>();
        var ignoredTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var badDates = 0;

        foreach (var row in rows)
        {
            if (row.Count <= Math.Max(dateCol, Math.Max(typeCol, valueCol))) continue;

            var metric = MatchMetric(row[typeCol]);
            if (metric is null)
            {
                if (!string.IsNullOrWhiteSpace(row[typeCol])) ignoredTypes.Add(row[typeCol]);
                continue;
            }

            if (!TryDate(row[dateCol], out var day)) { badDates++; continue; }
            if (!TryValue(row[valueCol], out var v)) continue;

            readings.Add(new ImportedReading(day, metric, Convert(metric, v, row[typeCol]), row[typeCol]));
        }

        if (badDates > 0) warnings.Add($"{badDates} row(s) skipped — the date could not be read.");

        // Several samples a day is the normal case in long format. Summed for counts,
        // averaged for rates: totalling a day's heart-rate readings would produce a
        // number in the thousands, and averaging a day's step counts would throw most
        // of them away.
        var collapsed = readings
            .GroupBy(r => (r.Day, r.Metric))
            .Select(g => new ImportedReading(
                g.Key.Day, g.Key.Metric,
                IsCumulative(g.Key.Metric) ? g.Sum(x => x.Value) : g.Average(x => x.Value),
                g.First().SourceColumn))
            .ToList();

        if (readings.Count != collapsed.Count)
            warnings.Add($"{readings.Count} samples collapsed to {collapsed.Count} daily values " +
                         "(counts summed, rates averaged).");

        var mapped = collapsed
            .GroupBy(r => r.Metric)
            .Select(g => new MappedColumn(g.First().SourceColumn, g.Key, g.Count()))
            .ToList();

        return Build(collapsed, "long", rows.Count, mapped, ignoredTypes.Take(25).ToList(), warnings);
    }

    // Things that accumulate over a day versus things that are a level at a moment.
    private static bool IsCumulative(string metric) =>
        metric is MetricKeys.Steps or MetricKeys.ActiveCalories or MetricKeys.TotalSleepMinutes;

    private static ImportPreview Build(
        List<ImportedReading> readings, string shape, int rowsRead,
        List<MappedColumn> mapped, List<string> ignored, List<string> warnings)
    {
        // Row counts filled in from what was actually extracted, not from the column
        // being present -- a recognised column that yielded nothing is the single most
        // useful thing this preview can surface.
        var counts = readings.GroupBy(r => r.Metric).ToDictionary(g => g.Key, g => g.Count());
        var withCounts = mapped
            .Select(m => new MappedColumn(m.Column, m.Metric, counts.TryGetValue(m.Metric, out var n) ? n : 0))
            .OrderByDescending(m => m.Rows)
            .ToList();

        foreach (var dead in withCounts.Where(m => m.Rows == 0))
            warnings.Add($"\"{dead.Column}\" was recognised as {dead.Metric} but no readable values were found in it.");

        if (readings.Count == 0 && warnings.Count == 0)
            warnings.Add("Nothing was imported — no column matched a metric this system tracks.");

        return new ImportPreview(
            shape, rowsRead,
            readings.Count == 0 ? null : readings.Min(r => r.Day),
            readings.Count == 0 ? null : readings.Max(r => r.Day),
            withCounts, ignored, warnings, readings);
    }

    // ── Units ───────────────────────────────────────────────────────────────────

    // Where a header says what unit it is in, honour it. Guessing is worse than not
    // converting: weight stored in pounds as though it were kilos reads as a plausible
    // number, and every baseline built on it is quietly wrong.
    private static double Convert(string metric, double value, string header)
    {
        var n = Normalise(header);

        if (metric == MetricKeys.WeightKg && (n.Contains("lb") || n.Contains("pound")))
            return value / 2.20462;

        // Sleep arrives in hours from most exporters and in minutes from a few.
        // Anything under 24 has to be hours -- nobody sleeps 24 minutes a night and
        // nobody sleeps 1400 hours.
        if (metric == MetricKeys.TotalSleepMinutes)
        {
            if (n.Contains("min")) return value;
            if (n.Contains("hour") || n.Contains("hr") || value <= 24) return value * 60;
        }

        return value;
    }

    // ── Cell readers ────────────────────────────────────────────────────────────

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "dd-MM-yyyy", "dd/MM/yyyy", "MM/dd/yyyy",
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss zzz", "yyyy-MM-dd'T'HH:mm:ss",
        "dd-MMM-yyyy", "d MMM yyyy", "MMM d, yyyy",
    ];

    internal static bool TryDate(string raw, out DateOnly day)
    {
        day = default;
        var s = raw.Trim();
        if (s.Length == 0) return false;

        // A timestamp with a zone is common in long exports. The DATE is what matters
        // and it is the local date the phone recorded, so the offset is kept rather
        // than normalised to UTC -- doing otherwise files a late-evening reading under
        // the next day.
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var dto))
        {
            day = DateOnly.FromDateTime(dto.DateTime);
            return true;
        }

        foreach (var f in DateFormats)
            if (DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                day = DateOnly.FromDateTime(dt);
                return true;
            }

        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var any)
            && Set(out day, DateOnly.FromDateTime(any));
    }

    private static bool Set(out DateOnly target, DateOnly value) { target = value; return true; }

    internal static bool TryValue(string raw, out double value)
    {
        value = 0;
        var s = raw.Trim();
        if (s.Length == 0) return false;

        // Empty markers from various exporters. Treated as absent rather than zero:
        // a missing HRV is not an HRV of zero, and a baseline that averages them in is
        // describing someone else.
        if (s is "-" or "--" or "NA" or "N/A" or "null" or "NaN") return false;

        s = s.Replace(",", "").Replace("%", "").Trim();

        // "7:24" as hours:minutes, which some sleep exporters emit.
        if (s.Contains(':') && TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var ts))
        {
            value = ts.TotalHours;
            return true;
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // ── File readers ────────────────────────────────────────────────────────────

    private static List<List<string>> ReadXlsx(Stream file)
    {
        using var wb = new XLWorkbook(file);
        var sheet = wb.Worksheets.First();
        var rows = new List<List<string>>();

        foreach (var row in sheet.RowsUsed())
        {
            if (rows.Count > MaxRows) break;

            var cells = new List<string>();
            foreach (var cell in row.Cells(1, row.LastCellUsed()?.Address.ColumnNumber ?? 1))
            {
                // Dates come back as a DateTime rather than the serial number they are
                // stored as. Reading the raw value here would file everything in 1900.
                cells.Add(cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var dt)
                    ? dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    : cell.GetFormattedString());
            }

            rows.Add(cells);
        }

        return rows;
    }

    // Enough CSV for a file an export app wrote: quoted fields, doubled quotes inside
    // them, commas and newlines within quotes. Tab and semicolon delimiters are
    // detected because European exports use them and a semicolon file parsed as commas
    // arrives as one enormous column.
    internal static List<List<string>> ReadDelimited(Stream file)
    {
        using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        var firstLine = text.Split('\n')[0];
        var delimiter = Delimiter(firstLine);

        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(c);
                continue;
            }

            if (c == '"') { quoted = true; }
            else if (c == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();

                // Blank lines are common at the end of an export and between sections.
                if (row.Any(f => f.Trim().Length > 0)) rows.Add(row);
                row = [];

                if (rows.Count > MaxRows) return rows;
            }
            else field.Append(c);
        }

        row.Add(field.ToString());
        if (row.Any(f => f.Trim().Length > 0)) rows.Add(row);

        return rows;
    }

    private static char Delimiter(string headerLine)
    {
        var counts = new[] { ',', '\t', ';' }.Select(d => (Delim: d, N: headerLine.Count(c => c == d)));
        var best = counts.OrderByDescending(x => x.N).First();
        return best.N == 0 ? ',' : best.Delim;
    }
}
