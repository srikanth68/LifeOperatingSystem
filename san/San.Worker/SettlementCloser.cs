using Microsoft.Extensions.Logging;
using San.Application;
using San.Application.Interfaces;

namespace San.Worker;

// Retires the reminders and tasks an email has just made pointless.
//
// The other half of email triage. Raising obligations was always there; closing them
// was not, so a paid bill kept its reminder and the reminder kept arriving -- which is
// most of how a notification channel becomes one you mute.
//
// The model only says WHAT was settled. Everything from there is deterministic: match
// against what is open, complete the matches, tell the user what was closed. The model
// never picks a GUID, because a wrongly-completed bill reminder is a missed payment,
// and that is a worse failure than the nagging it replaces.
//
// Completes rather than deletes, always. Everything here is reversible from the app or
// the website, and a wrong close should cost a tap to undo rather than being gone.
public static class SettlementCloser
{
    public static async Task<int> ApplyAsync(
        IReadOnlyList<Settlement> settlements,
        ISanRepository repo,
        ITelegramNotifier telegram,
        IModuleContextService moduleContext,
        ILogger logger,
        CancellationToken ct)
    {
        if (settlements.Count == 0) return 0;

        var openReminders = (await repo.GetRemindersAsync()).Where(r => !r.Done).ToList();
        var openActions = await moduleContext.GetOpenCommitmentsAsync(ct);
        var closedLines = new List<string>();

        foreach (var s in settlements)
        {
            foreach (var r in Settlements.MatchesIn(s, openReminders, x => x.Text))
            {
                await repo.UpdateReminderAsync(r.Id, x => x.Done = true);
                closedLines.Add($"reminder \"{r.Text}\"");
                logger.LogInformation("Settled by email ({Vendor}): completed reminder \"{Text}\".", s.Vendor, r.Text);
            }

            // NorthStar action items live in another module, so they close over HTTP.
            // Only northstar-sourced commitments have an id this can act on; Aasthi
            // property tasks come through the same list and are left alone.
            foreach (var c in Settlements.MatchesIn(s, openActions, x => x.Title))
            {
                if (!c.Source.Equals("northstar", StringComparison.OrdinalIgnoreCase)) continue;
                var id = c.Key.Split('.').LastOrDefault();
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (await moduleContext.CompleteActionAsync(id!, ct))
                {
                    closedLines.Add($"task \"{c.Title}\"");
                    logger.LogInformation("Settled by email ({Vendor}): completed action \"{Title}\".", s.Vendor, c.Title);
                }
            }

            // Recorded with an absolute date and no relative words -- this is exactly the
            // kind of line that used to be written as "paid the bill today" and then read
            // back months later as though it were still today.
            var amount = s.Amount is { } a ? $" of {a:0.00}" : "";
            await moduleContext.SaveMemoryAsync(
                $"On {DateTime.UtcNow:yyyy-MM-dd}, email confirmed the {s.Vendor} {s.What ?? "obligation"}{amount} was settled.",
                "event", 3, ct);
        }

        if (closedLines.Count == 0)
        {
            // Worth logging loudly: the model believed something was settled and nothing
            // matched. Either the wording drifted or the obligation was never tracked,
            // and both are things to notice rather than swallow.
            logger.LogInformation("Settlements reported ({Count}) but nothing open matched: {Vendors}.",
                settlements.Count, string.Join(", ", settlements.Select(x => x.Vendor)));
            return 0;
        }

        // Announced, not silent. Anything that closes an obligation on the user's behalf
        // has to say so -- a reminder that vanishes without explanation is worse than one
        // that arrives too often, because the user cannot tell whether it was handled.
        await telegram.SendAsync(
            "✅ Closed from email:\n" + string.Join("\n", closedLines.Select(l => "• " + l)), ct);

        return closedLines.Count;
    }
}
