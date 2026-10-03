using Vault.Worker.Models;
using Vault.Worker.Services;

namespace Vault.Tests;

// Finding subscriptions in a statement, and — mostly — refusing to.
//
// The hard part is not spotting Netflix. It is not calling the weekly supermarket a
// subscription, because a list with the supermarket in it is a list nobody trusts, and
// an untrusted list of recurring charges is worse than none: it gets dismissed along
// with the £180-a-year thing nobody meant to keep paying for.
public class RecurringChargeTests
{
    private static readonly DateTime Today = new(2026, 10, 3);

    private static Transaction Tx(string merchant, decimal amount, DateTime date, string? category = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        PlaidTransactionId = Guid.NewGuid().ToString(),
        AccountId = "acc",
        MerchantName = merchant,
        Description = merchant,
        Amount = amount,
        TransactionDate = date,
        Category = category,
        IsPending = false,
    };

    // n charges every `everyDays`, counting back from the most recent.
    private static List<Transaction> Series(
        string merchant, decimal amount, int everyDays, int count, DateTime? last = null, string? category = null)
    {
        var end = last ?? Today.AddDays(-3);
        return Enumerable.Range(0, count)
            .Select(i => Tx(merchant, amount, end.AddDays(-i * everyDays), category))
            .ToList();
    }

    // ── What it should find ─────────────────────────────────────────────────────

    [Fact]
    public void A_monthly_charge_at_a_steady_amount_is_recurring()
    {
        var found = RecurringCharges.Find(Series("Netflix", 11.99m, 30, 8), Today);

        var charge = Assert.Single(found.Charges);
        Assert.Equal("Netflix", charge.Merchant);
        Assert.Equal("monthly", charge.Cadence);
        Assert.Equal(11.99m, charge.Amount);
        Assert.Equal(8, charge.Occurrences);
    }

    [Fact]
    public void The_annual_cost_is_the_number_nobody_has()
    {
        var found = RecurringCharges.Find(Series("Netflix", 11.99m, 30, 8), Today);

        // 11.99 every 30 days is more than twelve payments a year, and rounding that to
        // "times twelve" is the error that makes the total comfortable.
        Assert.Equal(145.88m, Assert.Single(found.Charges).AnnualCost);
        Assert.Equal(145.88m, found.AnnualTotal);
    }

    [Fact]
    public void A_yearly_renewal_is_caught_when_the_window_is_long_enough()
    {
        var found = RecurringCharges.Find(Series("Amazon Prime", 95m, 365, 4, Today.AddDays(-20)), Today);

        Assert.Equal("yearly", Assert.Single(found.Charges).Cadence);
    }

    [Fact]
    public void Weekly_and_quarterly_are_both_rhythms()
    {
        var weekly = RecurringCharges.Find(Series("Gym", 12m, 7, 10), Today);
        var quarterly = RecurringCharges.Find(Series("Water", 48m, 91, 5), Today);

        Assert.Equal("weekly", Assert.Single(weekly.Charges).Cadence);
        Assert.Equal("quarterly", Assert.Single(quarterly.Charges).Cadence);
    }

    // ── What it must refuse ─────────────────────────────────────────────────────

    [Fact]
    public void Three_charges_are_not_a_pattern()
    {
        // Three is two intervals, and two intervals of similar length happen by chance.
        Assert.Empty(RecurringCharges.Find(Series("Netflix", 11.99m, 30, 3), Today).Charges);
    }

    [Fact]
    public void A_supermarket_is_not_a_subscription()
    {
        // Roughly weekly, wildly different amounts. This is the case that decides
        // whether anyone trusts the list.
        var rng = new Random(7);
        var shopping = Enumerable.Range(0, 20)
            .Select(i => Tx("Tesco", 14m + (decimal)rng.NextDouble() * 90m, Today.AddDays(-3 - i * 7)))
            .ToList();

        Assert.Empty(RecurringCharges.Find(shopping, Today).Charges);
    }

    [Fact]
    public void A_merchant_with_a_plausible_average_and_no_rhythm_is_refused()
    {
        // Four charges in one month then nothing for a year averages out to something
        // monthly-ish and is not a subscription.
        var bursty = new List<Transaction>
        {
            Tx("Someone", 20m, Today.AddDays(-400)),
            Tx("Someone", 20m, Today.AddDays(-393)),
            Tx("Someone", 20m, Today.AddDays(-386)),
            Tx("Someone", 20m, Today.AddDays(-10)),
        };

        Assert.Empty(RecurringCharges.Find(bursty, Today).Charges);
    }

    [Fact]
    public void Money_coming_in_is_not_a_subscription()
    {
        // Salary is the most regular transaction anybody has.
        var salary = Series("Employer", -3200m, 30, 10);
        Assert.Empty(RecurringCharges.Find(salary, Today).Charges);
    }

    [Fact]
    public void Pending_rows_are_ignored()
    {
        var charges = Series("Netflix", 11.99m, 30, 8);
        foreach (var c in charges) c.IsPending = true;

        Assert.Empty(RecurringCharges.Find(charges, Today).Charges);
    }

    [Fact]
    public void Two_charges_on_one_day_are_not_two_occurrences()
    {
        var charges = Series("Netflix", 11.99m, 30, 8);
        charges.Add(Tx("Netflix", 11.99m, charges[0].TransactionDate));

        Assert.Equal(8, Assert.Single(RecurringCharges.Find(charges, Today).Charges).Occurrences);
    }

    // ── The part people actually want ───────────────────────────────────────────

    [Fact]
    public void A_price_rise_is_found_and_dated()
    {
        // The thing that is invisible on a statement: the number changed once, months
        // ago, by a pound.
        var charges = Series("Spotify", 10.99m, 30, 8);
        charges[0].Amount = 11.99m;                      // the most recent
        charges[1].Amount = 11.99m;

        var found = RecurringCharges.Find(charges, Today);
        var rise = Assert.Single(found.PriceRises);

        Assert.Equal(11.99m, rise.Amount);
        Assert.Equal(10.99m, rise.PreviousAmount);
        Assert.NotNull(rise.PriceChangedOn);
        Assert.Contains("Went up from", rise.Note);
    }

    [Fact]
    public void A_price_cut_is_reported_without_being_called_a_rise()
    {
        var charges = Series("Insurance", 60m, 30, 8);
        charges[0].Amount = 45m;
        charges[1].Amount = 45m;

        var found = RecurringCharges.Find(charges, Today);

        Assert.Empty(found.PriceRises);
        Assert.Contains("Came down from", found.Charges[0].Note);
    }

    [Fact]
    public void A_charge_that_stopped_arriving_is_called_lapsed_not_active()
    {
        // Either a cancellation they made, or one they did not. Both are worth seeing,
        // and neither should be counted in what they spend a year.
        var found = RecurringCharges.Find(
            Series("Old Gym", 40m, 30, 6, Today.AddDays(-120)), Today);

        var charge = Assert.Single(found.Charges);
        Assert.Equal("lapsed", charge.Status);
        Assert.Single(found.Lapsed);
        Assert.Equal(0, found.AnnualTotal);          // not counted in the running cost
        Assert.Contains("cancelled, or the card changed", charge.Note);
    }

    [Fact]
    public void Something_billing_within_the_week_is_flagged_as_due()
    {
        var found = RecurringCharges.Find(
            Series("Rent", 1400m, 30, 6, Today.AddDays(-27)), Today);

        Assert.Single(found.DueSoon);
        Assert.Equal("due", found.Charges[0].Status);
    }

    [Fact]
    public void The_most_expensive_comes_first()
    {
        var all = Series("Netflix", 11.99m, 30, 8)
            .Concat(Series("Insurance", 90m, 30, 8))
            .Concat(Series("Coffee Club", 5m, 30, 8))
            .ToList();

        var found = RecurringCharges.Find(all, Today);

        Assert.Equal(["Insurance", "Netflix", "Coffee Club"], found.Charges.Select(c => c.Merchant));
    }

    [Fact]
    public void Statement_noise_does_not_split_one_merchant_into_three()
    {
        // The same company arrives spelled three ways. Splitting it produces three
        // merchants with too few occurrences each, and the subscription vanishes.
        var charges = Series("NETFLIX.COM 866-579-7172 CA", 11.99m, 30, 4)
            .Concat(Series("NETFLIX.COM", 11.99m, 30, 4, Today.AddDays(-123)))
            .ToList();

        Assert.Single(RecurringCharges.Find(charges, Today).Charges);
    }

    // ── Saying nothing, carefully ───────────────────────────────────────────────

    [Fact]
    public void An_empty_statement_says_what_absence_means()
    {
        var found = RecurringCharges.Find([], Today);

        Assert.Empty(found.Charges);
        Assert.Contains("not that none exist", found.Verdict);
    }

    [Fact]
    public void The_verdict_counts_what_was_found()
    {
        var all = Series("Netflix", 11.99m, 30, 8).Concat(Series("Gym", 40m, 30, 8)).ToList();
        all[0].Amount = 13.99m;
        all[1].Amount = 13.99m;

        var found = RecurringCharges.Find(all, Today);

        Assert.Contains("2 repeating charges", found.Verdict);
        Assert.Contains("gone up in price", found.Verdict);
    }
}
