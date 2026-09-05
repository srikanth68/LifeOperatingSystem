namespace Aasthi.Domain.Entities;

// What a property is due to receive or pay, on a schedule.
//
// The concept Aasthi never had. PropertyFinancialEntry records what happened, so
// nothing could answer "did the rent arrive?" -- absence of a record was
// indistinguishable from absence of a rule. This supplies the expectation that makes
// a missing payment visible.
//
// It is also what keeps the model out of the monthly loop. Once a charge exists with
// a MatchHint, recognising this month's rent is arithmetic over amount, date and
// description. San is needed to work out the pattern the first time and to classify
// one-off spending; it is not needed, and should not be trusted, to decide every
// month whether a given $2,400 deposit was rent.
public class RecurringCharge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PropertyId { get; set; }

    public string Direction { get; set; } = "expense";   // income | expense
    public string Category { get; set; } = "other";      // rent | mortgage | hoa | insurance | tax | utility | other
    public decimal Amount { get; set; }

    public string Frequency { get; set; } = "monthly";   // monthly | quarterly | annual

    // Day of the month it falls due. Clamped to the length of each month, so a charge
    // due on the 31st lands on the 28th in February rather than being skipped.
    public int DueDay { get; set; } = 1;

    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    // What this looks like in the bank feed -- "WELLS FARGO HOME MTG", "ZELLE FROM J
    // SMITH". Free text on purpose: rent arriving by Zelle carries the payer's name
    // and nothing else useful, and only the user (or San, reading the history once)
    // knows that "J SMITH" is the Scoter Street tenant.
    public string? MatchHint { get; set; }

    // A charge that has ended stops generating expectations without losing the history
    // of what it explained.
    public bool Active { get; set; } = true;

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Property Property { get; set; } = null!;

    // Every date this charge falls due within a window, inclusive.
    //
    // Computed rather than stored. Twelve rent rows per property per year, existing
    // only so that most of them can be absent, is a lot of records to keep correct
    // when a lease changes -- and the answer is cheap to derive from the rule.
    public IEnumerable<DateOnly> OccurrencesBetween(DateOnly from, DateOnly to)
    {
        var step = Frequency switch { "annual" => 12, "quarterly" => 3, _ => 1 };

        // Walk months from the start rather than from the window, so quarterly and
        // annual charges keep their anchor: a charge starting in February recurs in
        // May and August, not in whichever month the caller happened to ask about.
        var cursor = new DateOnly(StartDate.Year, StartDate.Month, 1);
        var stop = EndDate is { } e && e < to ? e : to;

        while (true)
        {
            var due = OnDay(cursor, DueDay);
            if (due > stop) yield break;
            if (due >= from && due >= StartDate) yield return due;
            cursor = cursor.AddMonths(step);
        }
    }

    private static DateOnly OnDay(DateOnly month, int day) =>
        new(month.Year, month.Month,
            Math.Min(day < 1 ? 1 : day, DateTime.DaysInMonth(month.Year, month.Month)));
}
