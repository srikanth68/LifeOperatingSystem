using Aasthi.Application;
using Aasthi.Application.Interfaces;

namespace Aasthi.Tests;

// The detector's job is to turn a year of bank noise into a short list worth reading.
// Two ways it can fail: miss a real charge (the user enters it by hand, mildly
// annoying), or offer up ordinary spending as a recurring obligation (the list becomes
// junk and stops being read). The second is the one that kills the feature, so most of
// these tests are about what must NOT be detected.
public class RecurringChargeDetectorTests
{
    private static VaultTransaction Tx(string id, decimal amount, string date, string desc) =>
        new(id, amount, DateOnly.Parse(date), desc, null);

    // Vault convention: positive is money out.
    private static List<VaultTransaction> Monthly(
        string desc, decimal amount, bool moneyOut, int months = 6, int day = 1, string? suffix = null)
    {
        var list = new List<VaultTransaction>();
        for (var i = 0; i < months; i++)
        {
            var d = new DateOnly(2025, 1, day).AddMonths(i);
            list.Add(new VaultTransaction(
                $"{desc}-{i}", moneyOut ? amount : -amount, d,
                suffix is null ? desc : $"{desc} {suffix}{1000 + i}", null));
        }
        return list;
    }

    [Fact]
    public void FindsMonthlyRentComingIn()
    {
        var found = RecurringChargeDetector.Detect(
            Monthly("ZELLE FROM J SMITH", 2400m, moneyOut: false, day: 3, suffix: "REF "));

        var rent = Assert.Single(found);
        Assert.Equal("income", rent.Direction);
        Assert.Equal("monthly", rent.Frequency);
        Assert.Equal(2400m, rent.Amount);
        Assert.Equal(3, rent.DueDay);
        Assert.Equal(0m, rent.AmountVariability);
    }

    [Fact]
    public void TheHintItProducesActuallyMatchesTheTransactionsItCameFrom()
    {
        // The whole point of the hint is to be fed to TransactionMatcher. A hint that
        // included the reference number would reject every future occurrence.
        var txs = Monthly("WELLS FARGO HOME MTG", 1850m, moneyOut: true, suffix: "AUTOPAY ");
        var hint = RecurringChargeDetector.Detect(txs).Single().MatchHint;

        Assert.DoesNotContain(hint.Split(' '), w => w.Any(char.IsDigit));
        foreach (var t in txs)
            Assert.True(hint.Split(' ').All(w => t.Haystack.Contains(w)), $"hint '{hint}' missed '{t.Description}'");
    }

    [Fact]
    public void ReferenceNumbersDoNotSplitOneChargeIntoMany()
    {
        // Each description is unique because of the trailing ref. Grouping on the raw
        // text would produce six groups of one and detect nothing at all.
        var found = RecurringChargeDetector.Detect(
            Monthly("SPECTRUM INTERNET", 79.99m, moneyOut: true, suffix: "INV"));

        Assert.Single(found);
        Assert.Equal(6, found[0].Occurrences);
    }

    [Fact]
    public void FindsAQuarterlyHoa()
    {
        var txs = new List<VaultTransaction>
        {
            Tx("h1", 340m, "2025-01-15", "SUNRIDGE HOA DUES"),
            Tx("h2", 340m, "2025-04-15", "SUNRIDGE HOA DUES"),
            Tx("h3", 340m, "2025-07-15", "SUNRIDGE HOA DUES"),
            Tx("h4", 340m, "2025-10-15", "SUNRIDGE HOA DUES"),
        };

        var hoa = Assert.Single(RecurringChargeDetector.Detect(txs));
        Assert.Equal("quarterly", hoa.Frequency);
        Assert.Equal(15, hoa.DueDay);
    }

    [Fact]
    public void FindsAnAnnualPremium()
    {
        var txs = new List<VaultTransaction>
        {
            Tx("i1", 1420m, "2023-06-10", "STATE FARM INSURANCE"),
            Tx("i2", 1465m, "2024-06-12", "STATE FARM INSURANCE"),
            Tx("i3", 1510m, "2025-06-11", "STATE FARM INSURANCE"),
        };

        var ins = Assert.Single(RecurringChargeDetector.Detect(txs));
        Assert.Equal("annual", ins.Frequency);
        Assert.True(ins.AmountVariability > 0);   // premiums drift; the amount is an estimate
    }

    [Fact]
    public void OrdinaryShoppingIsNotARecurringCharge()
    {
        // Frequent, same merchant, irregular gaps and amounts. This is the case that
        // decides whether the proposal list is worth reading.
        var txs = new List<VaultTransaction>
        {
            Tx("a1", 24.10m, "2025-01-03", "AMAZON MARKETPLACE"),
            Tx("a2", 119.99m, "2025-01-07", "AMAZON MARKETPLACE"),
            Tx("a3", 8.50m, "2025-01-26", "AMAZON MARKETPLACE"),
            Tx("a4", 62.00m, "2025-02-02", "AMAZON MARKETPLACE"),
            Tx("a5", 15.75m, "2025-02-04", "AMAZON MARKETPLACE"),
            Tx("a6", 210.00m, "2025-03-19", "AMAZON MARKETPLACE"),
        };

        Assert.Empty(RecurringChargeDetector.Detect(txs));
    }

    [Fact]
    public void TwoOccurrencesIsNotYetAPattern()
    {
        var txs = Monthly("SOME VENDOR", 100m, moneyOut: true, months: 2);
        Assert.Empty(RecurringChargeDetector.Detect(txs));
    }

    [Fact]
    public void AOneOffRepairIsNeverProposed()
    {
        var txs = new List<VaultTransaction> { Tx("r1", 340m, "2025-03-14", "HOME DEPOT 4471") };
        Assert.Empty(RecurringChargeDetector.Detect(txs));
    }

    [Fact]
    public void AVariableUtilityIsStillFoundButFlaggedAsVariable()
    {
        var txs = new List<VaultTransaction>
        {
            Tx("u1", 142m, "2025-01-20", "CITY WATER DEPT"),
            Tx("u2", 96m, "2025-02-19", "CITY WATER DEPT"),
            Tx("u3", 178m, "2025-03-20", "CITY WATER DEPT"),
            Tx("u4", 121m, "2025-04-21", "CITY WATER DEPT"),
        };

        var water = Assert.Single(RecurringChargeDetector.Detect(txs));
        Assert.Equal("monthly", water.Frequency);
        Assert.True(water.AmountVariability > 0.5m);
    }

    [Fact]
    public void IncomeAndExpenseAtTheSameVendorStaySeparate()
    {
        // A deposit from and a payment to the same counterparty are different
        // obligations and must never merge into one charge.
        var txs = Monthly("VENMO JSMITH", 500m, moneyOut: false)
            .Concat(Monthly("VENMO JSMITH", 500m, moneyOut: true)).ToList();

        var found = RecurringChargeDetector.Detect(txs);
        Assert.Equal(2, found.Count);
        Assert.Contains(found, c => c.Direction == "income");
        Assert.Contains(found, c => c.Direction == "expense");
    }

    [Fact]
    public void AMissedMonthDoesNotDestroyTheDetection()
    {
        // The tenant skipped April. Five payments still describe a monthly charge.
        var txs = Monthly("ZELLE FROM J SMITH", 2400m, moneyOut: false, months: 6)
            .Where(t => t.Date.Month != 4).ToList();

        var rent = Assert.Single(RecurringChargeDetector.Detect(txs));
        Assert.Equal("monthly", rent.Frequency);
        Assert.Equal(5, rent.Occurrences);
    }
}
