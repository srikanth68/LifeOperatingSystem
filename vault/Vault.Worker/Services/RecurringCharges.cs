using Vault.Worker.Models;

namespace Vault.Worker.Services;

// Finding the things that bill you whether you use them or not.
//
// Subscriptions are the only category of spending that is simultaneously small enough
// to ignore and large enough to matter: nobody notices $11.99, and nobody can tell you
// what they spend a year on things they forgot they bought. The bank statement has the
// answer and nobody reads it, because the answer is spread across twelve months in
// alphabetical company names.
//
// Detected rather than declared. A list the user maintains by hand goes stale the week
// after they make it; what makes this worth building is that it works out the answer
// from what already happened.
//
// Pure, so it can be tested against invented statements rather than against a live
// Plaid account. No clock either -- "today" comes in, because a function that reads
// DateTime.Now is one whose tests break in March.
public static class RecurringCharges
{
    public record Charge(
        string Merchant,
        decimal Amount,              // the current amount, positive = money out
        string Cadence,              // weekly | fortnightly | monthly | quarterly | yearly
        int IntervalDays,
        DateTime LastCharged,
        DateTime NextExpected,
        int Occurrences,
        decimal AnnualCost,
        string? Category,
        decimal? PreviousAmount,     // when the price changed
        DateTime? PriceChangedOn,
        string Status,               // active | due | overdue | lapsed
        string Note);

    public record Summary(
        IReadOnlyList<Charge> Charges,
        decimal MonthlyTotal,
        decimal AnnualTotal,
        IReadOnlyList<Charge> PriceRises,
        IReadOnlyList<Charge> DueSoon,
        IReadOnlyList<Charge> Lapsed,
        string Verdict);

    // Four occurrences before anything is called recurring. Three is two intervals, and
    // two intervals of roughly the same length happens by chance often enough -- a
    // fortnightly shop at the same supermarket would qualify, and calling that a
    // subscription makes the whole list untrustworthy.
    private const int MinOccurrences = 4;

    // How far a gap may drift from the cadence and still count as the same rhythm.
    // Monthly billing lands on different weekdays and February exists, so this cannot
    // be tight; too loose and every fortnightly habit becomes a subscription.
    private const double Tolerance = 0.25;

    private static readonly (string Name, int Days)[] Cadences =
    [
        ("weekly", 7),
        ("fortnightly", 14),
        ("monthly", 30),
        ("quarterly", 91),
        ("yearly", 365),
    ];

    public static Summary Find(IEnumerable<Transaction> transactions, DateTime today)
    {
        var charges = new List<Charge>();

        // Outgoings only, and only things with a merchant to group on. A positive
        // amount is money leaving in Plaid's convention, which is the opposite of what
        // everyone expects and is worth stating where it is relied on.
        var candidates = transactions
            .Where(t => !t.IsPending && t.Amount > 0)
            .GroupBy(t => Normalise(t.MerchantName ?? t.Description))
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() >= MinOccurrences);

        foreach (var group in candidates)
        {
            var dated = group.OrderBy(t => t.TransactionDate).ToList();

            // Several charges on one day are one event seen twice, or a shop visited
            // twice -- either way they are not a rhythm.
            var byDay = dated
                .GroupBy(t => t.TransactionDate.Date)
                .Select(g => g.OrderByDescending(t => t.Amount).First())
                .OrderBy(t => t.TransactionDate)
                .ToList();

            if (byDay.Count < MinOccurrences) continue;

            var gaps = byDay.Zip(byDay.Skip(1), (a, b) => (b.TransactionDate - a.TransactionDate).TotalDays).ToList();
            var median = Median(gaps);

            var cadence = Cadences.FirstOrDefault(c => Math.Abs(median - c.Days) <= c.Days * Tolerance);
            if (cadence.Name is null) continue;

            // Every gap has to fit, not just the average of them. A merchant charged
            // weekly for a month and then nothing for a year has a plausible mean and
            // no rhythm at all.
            if (gaps.Count(g => Math.Abs(g - cadence.Days) <= cadence.Days * Tolerance) < gaps.Count * 0.7) continue;

            // Amounts have to be stable too, or a supermarket becomes a subscription.
            var amounts = byDay.Select(t => t.Amount).ToList();
            var typical = Median(amounts.Select(a => (double)a).ToList());
            if (typical <= 0) continue;
            if (amounts.Count(a => Math.Abs((double)a - typical) <= typical * 0.2) < amounts.Count * 0.7) continue;

            var last = byDay[^1];
            var current = last.Amount;
            var next = last.TransactionDate.AddDays(cadence.Days);
            var overdueBy = (today.Date - next.Date).TotalDays;

            // A price rise is the most useful thing in here and the hardest to notice
            // on a statement: the number changed once, eight months ago, by two pounds.
            var earlier = byDay.Take(byDay.Count - 1).Select(t => t.Amount).ToList();
            var earlierTypical = earlier.Count > 0 ? (decimal)Median(earlier.Select(a => (double)a).ToList()) : current;

            decimal? previousAmount = null;
            DateTime? changedOn = null;

            if (earlier.Count >= 2 && Math.Abs(current - earlierTypical) > earlierTypical * 0.05m)
            {
                previousAmount = Math.Round(earlierTypical, 2);
                // When it changed: the first charge at the new level.
                changedOn = byDay.FirstOrDefault(t => Math.Abs(t.Amount - current) <= current * 0.02m)?.TransactionDate;
            }

            var status =
                overdueBy > cadence.Days * 1.5 ? "lapsed"
                : overdueBy > 0 ? "overdue"
                : overdueBy > -7 ? "due"
                : "active";

            charges.Add(new Charge(
                Merchant: Display(group.Key, dated),
                Amount: Math.Round(current, 2),
                Cadence: cadence.Name,
                IntervalDays: cadence.Days,
                LastCharged: last.TransactionDate,
                NextExpected: next,
                Occurrences: byDay.Count,
                AnnualCost: Math.Round(current * (365m / cadence.Days), 2),
                Category: last.Category,
                PreviousAmount: previousAmount,
                PriceChangedOn: changedOn,
                Status: status,
                Note: Note(status, cadence.Name, next, previousAmount, current, today)));
        }

        var ordered = charges.OrderByDescending(c => c.AnnualCost).ToList();
        var rises = ordered.Where(c => c.PreviousAmount is { } p && c.Amount > p).ToList();
        var due = ordered.Where(c => c.Status is "due" or "overdue").OrderBy(c => c.NextExpected).ToList();
        var lapsed = ordered.Where(c => c.Status == "lapsed").ToList();

        var annual = ordered.Where(c => c.Status != "lapsed").Sum(c => c.AnnualCost);

        return new Summary(
            ordered,
            Math.Round(annual / 12, 2),
            Math.Round(annual, 2),
            rises,
            due,
            lapsed,
            Verdict(ordered.Count, annual, rises.Count, lapsed.Count));
    }

    private static string Verdict(int count, decimal annual, int rises, int lapsed)
    {
        if (count == 0)
            return "No repeating charges found. That means none were detected in the transactions held, " +
                   "not that none exist — four occurrences at a steady interval are needed before " +
                   "anything is called recurring.";

        var parts = new List<string>
        {
            $"{count} repeating {(count == 1 ? "charge" : "charges")}, " +
            $"{annual:C0} a year between them",
        };

        if (rises > 0) parts.Add($"{rises} {(rises == 1 ? "has" : "have")} gone up in price");
        if (lapsed > 0) parts.Add($"{lapsed} stopped arriving and may have been cancelled");

        return string.Join("; ", parts) + ".";
    }

    private static string Note(string status, string cadence, DateTime next, decimal? previous, decimal current, DateTime today)
    {
        var when = status switch
        {
            "lapsed" => $"nothing since {next.AddDays(-30):d MMM} — cancelled, or the card changed",
            "overdue" => $"was expected {(today.Date - next.Date).Days} days ago",
            "due" => $"due {next:d MMM}",
            _ => $"next around {next:d MMM}",
        };

        if (previous is { } p && current > p)
            return $"{when}. Went up from {p:C} to {current:C}.";
        if (previous is { } q && current < q)
            return $"{when}. Came down from {q:C} to {current:C}.";

        return when + ".";
    }

    // Merchant names arrive with store numbers, cities and payment-processor noise
    // attached: "NETFLIX.COM 866-579-7172 CA" and "Netflix" are one company. Stripping
    // digits and trailing noise is crude and works; the alternative is a merchant table
    // nobody maintains.
    private static string Normalise(string raw)
    {
        var text = raw.ToLowerInvariant();

        foreach (var noise in new[] { "recurring", "autopay", "auto pay", "payment", "purchase", "pos ", "web id", "ach" })
            text = text.Replace(noise, " ");

        var cleaned = new string(text.Select(c => char.IsLetter(c) || c == ' ' ? c : ' ').ToArray());
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .Take(3);

        return string.Join(" ", words).Trim();
    }

    // The prettiest real name seen for this merchant, rather than the normalised key:
    // "Netflix" reads better than "netflix com".
    private static string Display(string key, List<Transaction> rows) =>
        rows.Select(r => r.MerchantName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .GroupBy(n => n!)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault()
        ?? string.Join(" ", key.Split(' ').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
