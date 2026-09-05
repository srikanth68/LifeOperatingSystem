using Aasthi.Application.Interfaces;

namespace Aasthi.Application;

// A repeating pattern found in the bank feed, offered as a possible recurring charge.
//
// Deliberately says nothing about WHICH property or what kind of charge it is. Finding
// that "SUNRIDGE HOA" hits every three months for $340 is arithmetic; knowing it is the
// Langer Street HOA is knowledge about the user's life. The first belongs here, the
// second belongs to San or to the user, and keeping them apart is what stops a
// clustering bug from silently mislabelling someone's tax records.
public record ChargeCandidate(
    string Direction,
    decimal Amount,
    string Frequency,
    int DueDay,
    string MatchHint,
    DateOnly FirstSeen,
    DateOnly LastSeen,
    int Occurrences,
    decimal AmountVariability,
    IReadOnlyList<string> SampleDescriptions,
    IReadOnlyList<string> TransactionIds);

// Finds the recurring charges hiding in a year of transactions.
//
// This exists so nobody has to hand-enter twelve charges and guess at what each one
// looks like in the bank feed. It is also what keeps a small model away from a large
// pile of financial rows: handing several hundred transactions to gemma-4-E4B and
// asking which repeat is exactly the setup that invents plausible numbers. Clustering
// is arithmetic and cannot hallucinate, so it runs first and reduces the problem to a
// dozen labelled patterns that a model can genuinely reason about.
public static class RecurringChargeDetector
{
    private const int MinOccurrences = 3;

    // Bands for classifying the median gap between occurrences. Wide enough to absorb
    // weekends and month-length differences, narrow enough that irregular spending
    // does not land in one by accident.
    private static readonly (string Name, int Low, int High)[] Bands =
    [
        ("monthly", 24, 38),
        ("quarterly", 80, 100),
        ("annual", 350, 380),
    ];

    public static List<ChargeCandidate> Detect(IEnumerable<VaultTransaction> transactions)
    {
        var groups = transactions
            .Where(t => t.Magnitude > 0)
            .GroupBy(t => (Key: Normalize(t.Description, t.MerchantName), t.IsMoneyOut))
            .Where(g => g.Key.Key.Length > 0 && g.Count() >= MinOccurrences);

        var found = new List<ChargeCandidate>();

        foreach (var group in groups)
        {
            var items = group.OrderBy(t => t.Date).ToList();

            var gaps = items.Zip(items.Skip(1), (a, b) => b.Date.DayNumber - a.Date.DayNumber)
                            .Where(d => d > 0)
                            .ToList();
            if (gaps.Count == 0) continue;

            var band = ClassifyGap(Median(gaps));
            if (band is null) continue;

            // Regularity is what separates a mortgage from a supermarket. Amazon appears
            // thirty times a year with gaps of 1, 4, 19, 2 days -- a median might land
            // in the monthly band by chance, but the individual gaps never agree.
            var regular = gaps.Count(g => g >= band.Value.Low && g <= band.Value.High);
            if (regular * 3 < gaps.Count * 2) continue;   // at least two thirds

            var amounts = items.Select(t => t.Magnitude).ToList();
            var median = Median(amounts);
            if (median <= 0) continue;

            found.Add(new ChargeCandidate(
                Direction: group.Key.IsMoneyOut ? "expense" : "income",
                Amount: median,
                Frequency: band.Value.Name,
                DueDay: (int)Math.Round(Median(items.Select(t => (decimal)t.Date.Day).ToList())),
                MatchHint: BuildHint(items),
                FirstSeen: items[0].Date,
                LastSeen: items[^1].Date,
                Occurrences: items.Count,
                // 0 means it is the same figure every time -- rent or a fixed mortgage.
                // A utility bill will be well above 0, which is a signal to the caller
                // that the amount is an estimate rather than a contract.
                AmountVariability: median == 0 ? 0 : (amounts.Max() - amounts.Min()) / median,
                SampleDescriptions: items.Select(t => t.Description).Distinct().Take(3).ToList(),
                TransactionIds: items.Select(t => t.Id).ToList()));
        }

        // Most confident first: many occurrences, steady amount.
        return found
            .OrderByDescending(c => c.Occurrences)
            .ThenBy(c => c.AmountVariability)
            .ToList();
    }

    // Strips everything a bank staples onto a description that changes between months:
    // reference numbers, dates, trailing digits, card fragments. What survives is the
    // stable identity -- "wells fargo home mtg", "zelle from j smith".
    private static string Normalize(string description, string? merchant)
    {
        var raw = $"{description} {merchant}".ToLowerInvariant();
        var words = raw
            .Split([' ', '\t', '-', '_', '/', '*', '#', '.', ',', ':', ';'], StringSplitOptions.RemoveEmptyEntries)
            // Any token containing a digit is a reference, a date or an account
            // fragment. None of them are stable, and all of them would split one
            // real charge into twelve groups of one.
            .Where(w => w.Length >= 2 && !w.Any(char.IsDigit))
            .Take(6)
            .ToList();

        return string.Join(" ", words);
    }

    // The hint the matcher will use later. Only words present in EVERY occurrence
    // qualify -- anything that varies between months would cause the hint to reject
    // the very transactions it was derived from.
    private static string BuildHint(List<VaultTransaction> items)
    {
        var perItem = items.Select(t => Normalize(t.Description, t.MerchantName)
                                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                        .ToHashSet())
                           .ToList();

        var common = perItem
            .Aggregate(new HashSet<string>(perItem[0]), (acc, next) => { acc.IntersectWith(next); return acc; });

        // Preserve the order they appear in, which reads far better than a set dump.
        var ordered = Normalize(items[0].Description, items[0].MerchantName)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(common.Contains)
            .Take(4);

        return string.Join(" ", ordered);
    }

    private static (string Name, int Low, int High)? ClassifyGap(decimal medianGap)
    {
        foreach (var b in Bands)
            if (medianGap >= b.Low && medianGap <= b.High) return b;
        return null;
    }

    private static decimal Median(List<int> values) => Median(values.Select(v => (decimal)v).ToList());

    private static decimal Median(List<decimal> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m;
    }
}
