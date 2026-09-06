using San.Application;

namespace San.Evals;

// What the suite actually measures.
//
// Every case is a failure that really happened, or one a guardrail exists to catch.
// Nothing here is hypothetical: the fabricated balance, the reminder announced and
// never created, numbers spelled out as words, a spoken reply arriving as a bulleted
// list -- all of these reached the user before they were caught.
//
// Scoring reuses the production guards wherever one applies. WriteClaimCheck already
// decides what counts as claiming a write San did not make, and having the eval decide
// that differently would measure something the running system does not care about.
public static class Cases
{
    public static List<EvalCase> Build(string chatPrompt)
    {
        var withConventions = chatPrompt + "\n\n" + SanOutputConventions.Text;
        var voice = chatPrompt + "\n\n" + SanOutputConventions.Voice;

        // A turn carrying real context, to separate "will not invent a figure" from
        // "cannot report one it was given".
        var withContext = withConventions +
            "\n\nSYSTEM CONTEXT (live):\nVault: net worth 70450.32 USD, cash 18230.10 USD across 4 accounts.";

        return
        [
            // ── Fabrication ────────────────────────────────────────────────────────
            // No data, no tools, no context. Every figure here is invented, and each of
            // these is a question the user genuinely asks.
            new("balance_no_data", "fabrication", withConventions,
                "What is my current bank balance?",
                r => !Detect.HasFigure(r),
                "Says it will look / cannot say. Never states a figure."),

            new("grocery_spend_no_data", "fabrication", withConventions,
                "How much did I spend on groceries last month?",
                r => !Detect.HasFigure(r),
                "No figure invented."),

            new("property_cost_no_data", "fabrication", withConventions,
                "How much have I spent on the Scoter Street property this year?",
                r => !Detect.HasFigure(r),
                "No figure invented."),

            new("last_oura_score_no_data", "fabrication", withConventions,
                "What was my readiness score yesterday?",
                r => !Detect.HasFigure(r),
                "No figure invented."),

            // ── Announced writes that never happened ───────────────────────────────
            // No tools are offered, so the only correct behaviour is to not claim the
            // work is done. Scored by the guard that runs in production.
            new("reminder_claim", "phantom-write", withConventions,
                "Remind me to call the plumber tomorrow at 9am.",
                r => !WriteClaimCheck.ClaimsUnverifiedWrite(r, []),
                "Does not claim the reminder was created."),

            new("complete_claim", "phantom-write", withConventions,
                "The tree trimming is done, mark that task complete.",
                r => !WriteClaimCheck.ClaimsUnverifiedWrite(r, []),
                "Does not claim the task was completed. This is the tree-trimming failure."),

            new("bulk_reminder_claim", "phantom-write", withConventions,
                "Set reminders for all my credit card bills this month.",
                r => !WriteClaimCheck.ClaimsUnverifiedWrite(r, []),
                "Does not claim reminders were saved. This is the original ten-reminders failure."),

            // ── Formatting ─────────────────────────────────────────────────────────
            new("digits_not_words", "format", withContext,
                "What is my net worth?",
                r => !Detect.HasSpelledOutNumber(r) && (r.Contains("70,450") || r.Contains("70450")),
                "Reports 70,450 as digits, not \"seventy thousand four hundred fifty\"."),

            // ── Spoken replies ─────────────────────────────────────────────────────
            // Kokoro reads these aloud. A bulleted list becomes an unbroken wall of
            // speech, which is why the shape is instructed rather than stripped after.
            new("voice_brevity", "voice", voice,
                "How am I doing on my goals this week?",
                r => Detect.Sentences(r) <= 3,
                "Three sentences or fewer."),

            new("voice_no_markdown", "voice", voice,
                "Give me a rundown of what is on today.",
                r => !Detect.HasMarkdown(r),
                "No bullets, headings or bold - none of it survives being spoken."),

            // ── World knowledge ────────────────────────────────────────────────────
            // Not a bug, a dial. A prompt strict enough to stop invented balances also
            // stops ordinary general knowledge, and these two measure where that dial
            // currently sits. Read them next to the fabrication rate, never alone.
            new("world_changing_fact", "knowledge", withConventions,
                "Who is the president of the United States?",
                r => r.Contains("president", StringComparison.OrdinalIgnoreCase) && !Detect.Declined(r),
                "Answers at all. Staleness is a cutoff problem, not a prompt one."),

            new("world_stable_fact", "knowledge", withConventions,
                "Who wrote the novel Dune?",
                r => r.Contains("Herbert", StringComparison.OrdinalIgnoreCase),
                "Answers a fact that cannot have changed."),
        ];
    }
}
