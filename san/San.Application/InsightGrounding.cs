using System.Globalization;
using System.Text.RegularExpressions;

namespace San.Application;

public record ProposedInsight(string Title, string Body);

// Checks that every figure an insight cites actually appears in the data it was shown.
//
// The deduction is the model's and should be — but gemma-4-E4B narrates numbers it did
// not compute, and an insight is uniquely dangerous to get wrong: it is written into
// NorthStar as a durable fact, resurfaces in chat context as something San "knows",
// and reads with more authority than the raw data ever did. "Spending is up 40% in
// weeks you sleep under 6h" is worthless if the 40% was improvised, and worse than
// worthless because it is unfalsifiable after the fact.
//
// So the model may say anything it likes about the SHAPE of the data; it may not
// introduce quantities that aren't in it. Rejected insights are dropped, not repaired
// — silently correcting a number would leave the surrounding claim intact while
// changing what it asserts.
public static class InsightGrounding
{
    // Bare integers 0–10 are almost always prose ("across 8 weeks", "3 of your habits")
    // rather than claims about the data, and demanding they appear verbatim rejects
    // perfectly good insights. Percentages and money are always checked.
    private const int SmallNumberCeiling = 10;

    private static readonly Regex Figure = new(
        @"(?<money>\$\s?\d[\d,]*(?:\.\d+)?)|(?<pct>\d+(?:\.\d+)?\s?%)|(?<num>\b\d[\d,]*(?:\.\d+)?\b)",
        RegexOptions.Compiled);

    public static bool IsGrounded(ProposedInsight insight, string sourceData, out string reason)
    {
        var numbers = ExtractSourceNumbers(sourceData);

        // Dates are stripped from the CLAIM as well as from the data. "the week of
        // 2026-07-27" is naming a week, not asserting a quantity, and treating its
        // parts as figures would reject an insight for correctly citing a real date.
        foreach (Match m in Figure.Matches(IsoDate.Replace($"{insight.Title} {insight.Body}", " ")))
        {
            var raw = m.Value.Trim();
            if (!TryParseFigure(raw, out var value)) continue;

            // A percentage is a derived claim. It will rarely appear verbatim in the
            // table, so it is checked against percentages the data actually supports —
            // which, for a table of counts and totals, means it must be derivable as a
            // ratio between two figures present in it.
            if (m.Groups["pct"].Success)
            {
                if (!IsPlausibleRatio(value, numbers))
                {
                    reason = $"cites {raw}, which is not a ratio of any two figures in the data";
                    return false;
                }
                continue;
            }

            if (m.Groups["num"].Success && value <= SmallNumberCeiling && value == Math.Floor(value))
                continue;

            if (!numbers.Any(n => Close(n, value)))
            {
                reason = $"cites {raw}, which does not appear in the data";
                return false;
            }
        }

        reason = "";
        return true;
    }

    // Dates are stripped before anything is harvested. "2026-07-13" otherwise
    // contributes 2026, 7 and 13 to the pool of "figures the data contains", which is
    // wrong twice over: it would let an insight cite 2026 as a quantity, and — far
    // worse — it inflates the pool enough that some pair of numbers divides into
    // almost any percentage, quietly disabling the ratio check.
    private static readonly Regex IsoDate = new(@"\d{4}-\d{2}-\d{2}(T[\d:.]+Z?)?", RegexOptions.Compiled);

    private static HashSet<decimal> ExtractSourceNumbers(string data)
    {
        var set = new HashSet<decimal>();
        foreach (Match m in Figure.Matches(IsoDate.Replace(data, " ")))
            if (TryParseFigure(m.Value, out var v)) set.Add(v);
        return set;
    }

    private static bool TryParseFigure(string raw, out decimal value) =>
        decimal.TryParse(
            raw.Replace("$", "").Replace("%", "").Replace(",", "").Trim(),
            NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    // Rounding: the model saying "40%" over a true 39.6% is honest reporting, not
    // invention. Tolerance is proportional so it scales with magnitude.
    private static bool Close(decimal a, decimal b)
    {
        if (a == b) return true;
        var scale = Math.Max(Math.Abs(a), Math.Abs(b));
        return Math.Abs(a - b) <= Math.Max(0.5m, scale * 0.02m);
    }

    // Is this percentage roughly the ratio of some pair of numbers in the data — either
    // as a share (a/b) or as a change ((a-b)/b)?
    private static bool IsPlausibleRatio(decimal pct, HashSet<decimal> numbers)
    {
        if (pct is < 0 or > 1000) return false;
        var vals = numbers.Where(n => n != 0).ToList();
        if (vals.Count < 2) return true;   // nothing to check against; don't invent a failure

        // Tighter than Close(): that scales at 2% of the value, which around a figure
        // like 400% is an eight-point window — wide enough that, across a few hundred
        // candidate pairs, something always lands inside it. A percentage claim has to
        // match what the data supports closely or it isn't derived from it.
        static bool CloseRatio(decimal claimed, decimal actual) =>
            Math.Abs(claimed - actual) <= Math.Max(1.5m, claimed * 0.03m);

        foreach (var a in vals)
            foreach (var b in vals)
            {
                if (a == b) continue;
                if (CloseRatio(pct, Math.Abs(a / b) * 100)) return true;
                if (CloseRatio(pct, Math.Abs((a - b) / b) * 100)) return true;
            }
        return false;
    }
}
