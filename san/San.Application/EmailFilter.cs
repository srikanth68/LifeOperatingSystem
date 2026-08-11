using San.Application.Interfaces;

namespace San.Application;

public record EmailVerdict(bool Keep, string Reason);

// Drops bulk mail BEFORE the model sees it.
//
// The triage prompt has always said "ignore newsletters, marketing, and routine
// notifications entirely" and gemma-4-E4B ignores that instruction — a $9 promo
// email comes back flagged as something to act on. This is the same lesson the
// notification ledger taught: when the model will not reliably follow a rule, the
// rule belongs outside the model. An instruction is a request; this is not.
//
// It also makes triage cheaper and faster, since marketing is most of the volume and
// every dropped message is one the model never has to read.
//
// The danger is obvious and is what most of this file is about: a filter that eats a
// real bill is far worse than the spam it prevents. So bulk-ness alone never drops a
// message — it has to be bulk AND show no sign of being transactional, and the user
// can always force a sender either way.
public static class EmailFilter
{
    public const string KeepSendersKey = "email.filter.keep_senders";
    public const string DropSendersKey = "email.filter.drop_senders";

    // Headers that mean "this was sent to a list, not to you". List-Unsubscribe is
    // RFC 2369 and is the single strongest signal: bulk senders are effectively
    // required to set it, and person-to-person mail never does.
    private static readonly string[] BulkHeaders =
    [
        "list-unsubscribe", "list-id", "list-post", "list-help",
        "x-campaign-id", "x-mailchimp-id", "feedback-id", "x-marketing-id",
    ];

    // Gmail's own classifier, which is considerably better than anything worth
    // rebuilding here. Only the two categories that are reliably not actionable —
    // CATEGORY_UPDATES carries receipts and statements, so it is deliberately absent.
    private static readonly string[] BulkLabels = ["CATEGORY_PROMOTIONS", "CATEGORY_SOCIAL"];

    // Words that survive contact with marketing copy. Deliberately excludes the
    // urgency vocabulary campaigns weaponise — "expires", "final notice", "act now",
    // "confirm" — because those appear more often in promotions than in real bills.
    private static readonly string[] TransactionalTerms =
    [
        "invoice", "statement", "receipt", "past due", "overdue", "payment failed",
        "payment declined", "autopay", "direct debit", "remittance",
        "verification code", "security alert", "suspicious", "unauthorized",
        "appointment", "policy number", "claim number", "explanation of benefits",
        "lease", "landlord", "tenant", "eviction", "closing disclosure",
        "tax", "irs", "court", "summons", "jury",
        "shipped", "out for delivery", "tracking number", "refund issued",
    ];

    public static EmailVerdict Classify(EmailMessage msg, string? keepSenders, string? dropSenders)
    {
        var from = (msg.From ?? "").ToLowerInvariant();

        // The user's own overrides win outright, in both directions. Somebody who has
        // been told twice that their bank's mail went missing should be able to end
        // the argument permanently.
        if (MatchesAny(from, dropSenders)) return new(false, "sender on your drop list");
        if (MatchesAny(from, keepSenders)) return new(true, "sender on your keep list");

        var bulkReason = BulkSignal(msg);
        if (bulkReason is null) return new(true, "not bulk mail");

        // Bulk AND transactional is the case that matters: statements, shipping
        // notices and policy renewals are genuinely sent from bulk infrastructure.
        // Treating the bulk signal as sufficient on its own is what would eat them.
        if (TransactionalHint(msg) is { } hint)
            return new(true, $"bulk ({bulkReason}) but reads as transactional — {hint}");

        return new(false, bulkReason);
    }

    private static string? BulkSignal(EmailMessage msg)
    {
        if (msg.Headers is { } h)
        {
            foreach (var name in BulkHeaders)
                if (h.ContainsKey(name)) return $"has {name} header";

            if (h.TryGetValue("precedence", out var prec)
                && (prec.Contains("bulk", StringComparison.OrdinalIgnoreCase)
                    || prec.Contains("junk", StringComparison.OrdinalIgnoreCase)
                    || prec.Contains("list", StringComparison.OrdinalIgnoreCase)))
                return $"Precedence: {prec.Trim()}";

            if (h.TryGetValue("auto-submitted", out var auto)
                && auto.StartsWith("auto", StringComparison.OrdinalIgnoreCase)
                && !auto.Equals("no", StringComparison.OrdinalIgnoreCase))
                return "auto-submitted";
        }

        if (msg.Labels is { } labels)
            foreach (var l in BulkLabels)
                if (labels.Contains(l, StringComparer.OrdinalIgnoreCase))
                    return $"Gmail classified it as {l.Replace("CATEGORY_", "").ToLowerInvariant()}";

        return null;
    }

    private static string? TransactionalHint(EmailMessage msg)
    {
        var text = $"{msg.Subject} {msg.Snippet}".ToLowerInvariant();
        foreach (var term in TransactionalTerms)
            if (text.Contains(term, StringComparison.Ordinal))
                return $"mentions \"{term}\"";
        return null;
    }

    // Substring match against the whole From line, so both a bare domain
    // ("chase.com") and a full address work as a rule.
    private static bool MatchesAny(string from, string? list)
    {
        if (string.IsNullOrWhiteSpace(list)) return false;
        foreach (var raw in list.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var needle = raw.ToLowerInvariant();
            if (needle.Length > 1 && from.Contains(needle, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
