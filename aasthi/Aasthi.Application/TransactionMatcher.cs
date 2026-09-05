using Aasthi.Application.Interfaces;
using Aasthi.Domain.Entities;

namespace Aasthi.Application;

// Deciding which bank transaction paid a recurring charge.
//
// Kept deterministic on purpose, and the reasoning is the same one behind
// SettlementCloser: a model is needed to work out the PATTERN once -- that "ZELLE FROM
// J SMITH" is the Scoter Street rent -- and is not needed to decide, every month, that
// this particular -2400 is that rent. Once the rule exists the question is arithmetic,
// and arithmetic that cannot hallucinate is worth more than arithmetic that is
// occasionally cleverer.
//
// The failure being guarded against is not a missed match. A missed match leaves a row
// saying "missing", which the user sees and fixes in a tap. A WRONG match silently
// records that rent arrived when it did not, and that error surfaces at tax time or
// never.
public static class TransactionMatcher
{
    // Above this a match is written as confirmed; below it, as pending. Reaching it
    // requires a MatchHint, so no charge auto-matches until the user (or the detector)
    // has said what the bank line actually looks like. Amount and date alone top out
    // at 80 -- close, deliberately not close enough.
    public const int AutoConfirmThreshold = 85;

    // Beyond these a candidate is not considered at all, at any score.
    private const int MaxDaysApart = 14;
    private const decimal MaxAmountDrift = 0.05m;

    public static (VaultTransaction Transaction, int Confidence)? Best(
        RecurringCharge charge, DateOnly due, IEnumerable<VaultTransaction> candidates)
    {
        var wantsMoneyOut = !charge.Direction.Equals("income", StringComparison.OrdinalIgnoreCase);

        var scored = candidates
            .Where(t => t.IsMoneyOut == wantsMoneyOut)
            .Select(t => (Transaction: t, Confidence: Score(charge, due, t)))
            .Where(x => x.Confidence > 0)
            .OrderByDescending(x => x.Confidence)
            .ToList();

        if (scored.Count == 0) return null;

        // Two candidates scoring identically means the evidence genuinely does not
        // distinguish them -- two properties with the same HOA fee billed the same day,
        // say. Picking one at that point is a coin toss with the user's tax records, so
        // hand both down as low confidence and let a human look.
        if (scored.Count > 1 && scored[0].Confidence == scored[1].Confidence)
            return (scored[0].Transaction, Math.Min(scored[0].Confidence, AutoConfirmThreshold - 1));

        return scored[0];
    }

    private static int Score(RecurringCharge charge, DateOnly due, VaultTransaction t)
    {
        var drift = charge.Amount == 0 ? 1m : Math.Abs(t.Magnitude - charge.Amount) / Math.Abs(charge.Amount);
        if (drift > MaxAmountDrift) return 0;

        var daysApart = Math.Abs(t.Date.DayNumber - due.DayNumber);
        if (daysApart > MaxDaysApart) return 0;

        // A hint that is set and not found is a rejection, not a lower score. The user
        // wrote it precisely to say "this and nothing else".
        var hintScore = 0;
        if (!string.IsNullOrWhiteSpace(charge.MatchHint))
        {
            if (!HintMatches(charge.MatchHint!, t.Haystack)) return 0;
            hintScore = 30;
        }

        var amountScore = drift switch
        {
            0m => 45,
            <= 0.01m => 35,
            <= 0.02m => 30,
            _ => 20,
        };

        var dateScore = daysApart switch
        {
            <= 2 => 35,
            <= 5 => 25,
            <= 10 => 15,
            _ => 5,
        };

        return Math.Min(100, amountScore + dateScore + hintScore);
    }

    // Every meaningful word of the hint has to appear somewhere in the description or
    // merchant. Substring matching on the whole hint would fail on the reference
    // numbers and dates banks staple onto descriptions, and matching ANY single word
    // would let "WELLS FARGO HOME MTG" match a Wells Fargo card payment.
    private static bool HintMatches(string hint, string haystack)
    {
        var words = hint.ToLowerInvariant()
            .Split([' ', '\t', '-', '_', '/', '*', '#'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3)
            .ToList();

        return words.Count != 0 && words.All(haystack.Contains);
    }
}
