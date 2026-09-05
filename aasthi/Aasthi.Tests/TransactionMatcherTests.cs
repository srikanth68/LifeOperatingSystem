using Aasthi.Application;
using Aasthi.Application.Interfaces;
using Aasthi.Domain.Entities;

namespace Aasthi.Tests;

// A missed match leaves a visible "missing" row the user fixes in one tap. A WRONG
// match records that rent arrived when it did not, and nobody finds out until tax
// time. So the negative cases below carry more weight than the positive ones.
public class TransactionMatcherTests
{
    private static RecurringCharge Rent(string? hint = null) => new()
    {
        Direction = "income", Category = "rent", Amount = 2400m,
        Frequency = "monthly", DueDay = 1, StartDate = new DateOnly(2025, 1, 1), MatchHint = hint,
    };

    private static RecurringCharge Mortgage(string? hint = null) => new()
    {
        Direction = "expense", Category = "mortgage", Amount = 1850m,
        Frequency = "monthly", DueDay = 1, StartDate = new DateOnly(2025, 1, 1), MatchHint = hint,
    };

    // Vault's convention: positive is money OUT. Rent arriving is negative.
    private static VaultTransaction MoneyIn(decimal amt, string date, string desc) =>
        new("tx1", -amt, DateOnly.Parse(date), desc, null);

    private static VaultTransaction MoneyOut(decimal amt, string date, string desc) =>
        new("tx2", amt, DateOnly.Parse(date), desc, null);

    private static readonly DateOnly Due = new(2025, 3, 1);

    [Fact]
    public void ExactRentWithAHintAutoConfirms()
    {
        var hit = TransactionMatcher.Best(Rent("ZELLE FROM J SMITH"), Due,
            [MoneyIn(2400m, "2025-03-01", "ZELLE FROM J SMITH REF 88213")]);

        Assert.NotNull(hit);
        Assert.True(hit!.Value.Confidence >= TransactionMatcher.AutoConfirmThreshold);
    }

    [Fact]
    public void WithoutAHintEvenAPerfectMatchStaysPending()
    {
        // Amount exact, date exact, direction right — and still below the bar. Nothing
        // auto-matches until someone has said what the bank line looks like.
        var hit = TransactionMatcher.Best(Rent(), Due, [MoneyIn(2400m, "2025-03-01", "DEPOSIT")]);

        Assert.NotNull(hit);
        Assert.True(hit!.Value.Confidence < TransactionMatcher.AutoConfirmThreshold);
    }

    [Fact]
    public void RentIsNeverSatisfiedByMoneyGoingOut()
    {
        // Same magnitude, wrong direction. Without the sign check a $2,400 outgoing
        // payment would close out the month's rent.
        Assert.Null(TransactionMatcher.Best(Rent("zelle"), Due,
            [MoneyOut(2400m, "2025-03-01", "ZELLE TO CONTRACTOR")]));
    }

    [Fact]
    public void AHintThatDoesNotAppearRejectsOutright()
    {
        // The amount and date are perfect. The user said this charge looks like Wells
        // Fargo; this is not Wells Fargo, so it is not the charge.
        Assert.Null(TransactionMatcher.Best(Mortgage("WELLS FARGO HOME MTG"), Due,
            [MoneyOut(1850m, "2025-03-01", "CHASE MORTGAGE PAYMENT")]));
    }

    [Fact]
    public void EveryHintWordMustAppearNotJustOne()
    {
        // "WELLS FARGO HOME MTG" must not match a Wells Fargo credit card payment.
        Assert.Null(TransactionMatcher.Best(Mortgage("WELLS FARGO HOME MTG"), Due,
            [MoneyOut(1850m, "2025-03-01", "WELLS FARGO CREDIT CARD PMT")]));
    }

    [Fact]
    public void BankReferenceNumbersDoNotBreakTheHint()
        => Assert.NotNull(TransactionMatcher.Best(Mortgage("WELLS FARGO HOME MTG"), Due,
            [MoneyOut(1850m, "2025-03-01", "WELLS FARGO HOME MTG 0093182 AUTOPAY")]));

    [Theory]
    [InlineData(2401.50)]   // a couple of dollars off — still the rent
    [InlineData(2350.00)]   // ~2% short
    public void SmallAmountDriftStillMatches(decimal amount)
        => Assert.NotNull(TransactionMatcher.Best(Rent("zelle smith"), Due,
            [MoneyIn(amount, "2025-03-01", "ZELLE FROM J SMITH")]));

    [Theory]
    [InlineData(2000.00)]   // way short — a partial payment is not the rent
    [InlineData(4800.00)]   // two months at once is not one month's rent
    public void LargeAmountDriftIsRejected(decimal amount)
        => Assert.Null(TransactionMatcher.Best(Rent("zelle smith"), Due,
            [MoneyIn(amount, "2025-03-01", "ZELLE FROM J SMITH")]));

    [Fact]
    public void ATransactionTooFarFromTheDueDateIsNotThisMonths()
    {
        // Three weeks late is next month's problem, not this occurrence's payment —
        // otherwise April's rent silently closes March.
        Assert.Null(TransactionMatcher.Best(Rent("zelle smith"), Due,
            [MoneyIn(2400m, "2025-03-25", "ZELLE FROM J SMITH")]));
    }

    [Fact]
    public void TwoIdenticalCandidatesNeverAutoConfirm()
    {
        // Two properties, same HOA fee, same day. The evidence does not distinguish
        // them, so a human decides rather than the matcher guessing.
        var hoa = new RecurringCharge
        {
            Direction = "expense", Category = "hoa", Amount = 340m,
            Frequency = "quarterly", DueDay = 1, StartDate = new DateOnly(2025, 1, 1),
            MatchHint = "SUNRIDGE HOA",
        };

        var hit = TransactionMatcher.Best(hoa, Due, [
            new VaultTransaction("a", 340m, Due, "SUNRIDGE HOA DUES", null),
            new VaultTransaction("b", 340m, Due, "SUNRIDGE HOA DUES", null),
        ]);

        Assert.NotNull(hit);
        Assert.True(hit!.Value.Confidence < TransactionMatcher.AutoConfirmThreshold);
    }

    [Fact]
    public void NoCandidatesMeansNoMatchNotAGuess()
        => Assert.Null(TransactionMatcher.Best(Rent("zelle"), Due, []));
}
