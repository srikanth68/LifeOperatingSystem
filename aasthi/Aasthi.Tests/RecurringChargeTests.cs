using Aasthi.Domain.Entities;

namespace Aasthi.Tests;

// The occurrence maths decides whether a missing rent payment is ever noticed, so the
// edge cases matter more than the happy path: a charge that silently skips February
// is a month of rent nobody chases.
public class RecurringChargeTests
{
    private static RecurringCharge Charge(string freq, int dueDay, string start, string? end = null) => new()
    {
        Frequency = freq,
        DueDay = dueDay,
        StartDate = DateOnly.Parse(start),
        EndDate = end is null ? null : DateOnly.Parse(end),
        Amount = 2400m,
    };

    private static string[] Between(RecurringCharge c, string from, string to) =>
        c.OccurrencesBetween(DateOnly.Parse(from), DateOnly.Parse(to))
         .Select(d => d.ToString("yyyy-MM-dd")).ToArray();

    [Fact]
    public void MonthlyRentFallsOnTheSameDayEachMonth()
    {
        var due = Between(Charge("monthly", 1, "2025-01-01"), "2025-01-01", "2025-03-31");
        Assert.Equal(["2025-01-01", "2025-02-01", "2025-03-01"], due);
    }

    [Fact]
    public void ADayThatDoesNotExistClampsToTheEndOfTheMonth()
    {
        // Due on the 31st. February has no 31st, and skipping the month entirely would
        // mean a charge that quietly never comes due.
        var due = Between(Charge("monthly", 31, "2025-01-31"), "2025-01-01", "2025-04-30");
        Assert.Equal(["2025-01-31", "2025-02-28", "2025-03-31", "2025-04-30"], due);
    }

    [Fact]
    public void LeapFebruaryGetsTheTwentyNinth()
        => Assert.Contains("2024-02-29", Between(Charge("monthly", 31, "2024-01-31"), "2024-01-01", "2024-03-01"));

    [Fact]
    public void QuarterlyKeepsItsAnchorRatherThanTheWindow()
    {
        // An HOA charge starting in February is due in May and August -- not in
        // whichever month the caller happened to ask about.
        var due = Between(Charge("quarterly", 15, "2025-02-15"), "2025-01-01", "2025-12-31");
        Assert.Equal(["2025-02-15", "2025-05-15", "2025-08-15", "2025-11-15"], due);
    }

    [Fact]
    public void AnnualRecursOnceAYear()
    {
        var due = Between(Charge("annual", 10, "2024-06-10"), "2024-01-01", "2026-12-31");
        Assert.Equal(["2024-06-10", "2025-06-10", "2026-06-10"], due);
    }

    [Fact]
    public void NothingIsDueBeforeTheChargeStarts()
    {
        // The window opens in January; the lease does not start until March.
        var due = Between(Charge("monthly", 1, "2025-03-01"), "2025-01-01", "2025-04-30");
        Assert.Equal(["2025-03-01", "2025-04-01"], due);
    }

    [Fact]
    public void AnEndedChargeStopsGeneratingOccurrences()
    {
        var due = Between(Charge("monthly", 1, "2025-01-01", end: "2025-02-15"), "2025-01-01", "2025-12-31");
        Assert.Equal(["2025-01-01", "2025-02-01"], due);
    }

    [Fact]
    public void AWindowEntirelyBeforeTheStartIsEmpty()
        => Assert.Empty(Between(Charge("monthly", 1, "2026-01-01"), "2025-01-01", "2025-12-31"));
}
