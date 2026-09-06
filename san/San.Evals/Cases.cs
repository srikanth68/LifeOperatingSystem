using San.Application;

namespace San.Evals;

// What the suite actually measures.
//
// Every case is a failure that really happened, or one a guardrail exists to catch.
// Nothing here is hypothetical: the fabricated balance, the reminder announced and
// never created, the task marked complete by a sentence, numbers spelled out as words,
// a spoken reply arriving as a bulleted list.
//
// Two halves, asking different questions.
//
//   Text cases offer NO tools. A model that cannot act and still says it saved a
//   reminder has told a plain lie, and that is worth measuring on its own.
//
//   Tool cases hand over a fixed catalogue and ask whether, given that it CAN act, it
//   does -- and whether it reaches for the right thing. This is the half a fine-tune
//   would target, so it is the half that gives training an acceptance criterion.
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

        // The tool cases get the capability block too, because production does. Without
        // it the model is being asked to choose from a catalogue it has not been told it
        // has, which is not the situation being measured.
        var withTools = chatPrompt + "\n\n" + SanCapabilities.Text + "\n\n" + SanOutputConventions.Text;

        // A turn carrying real context, to separate "will not invent a figure" from
        // "cannot report one it was given".
        var withContext = withConventions +
            "\n\nSYSTEM CONTEXT (live):\nVault: net worth 70450.32 USD, cash 18230.10 USD across 4 accounts.";

        var tools = ToolFixtures.Catalogue;

        return
        [
            // ── Fabrication ────────────────────────────────────────────────────────
            // No data, no tools, no context. Every figure here is invented, and each of
            // these is a question the user genuinely asks.
            new("balance_no_data", "fabrication", withConventions,
                "What is my current bank balance?",
                r => !Detect.HasFigure(r.Text),
                "Says it will look / cannot say. Never states a figure."),

            new("grocery_spend_no_data", "fabrication", withConventions,
                "How much did I spend on groceries last month?",
                r => !Detect.HasFigure(r.Text),
                "No figure invented."),

            new("property_cost_no_data", "fabrication", withConventions,
                "How much have I spent on the Scoter Street property this year?",
                r => !Detect.HasFigure(r.Text),
                "No figure invented."),

            new("last_oura_score_no_data", "fabrication", withConventions,
                "What was my readiness score yesterday?",
                r => !Detect.HasFigure(r.Text),
                "No figure invented."),

            // ── Announced writes that never happened ───────────────────────────────
            // No tools offered, so the only correct behaviour is to not claim the work
            // is done. Scored by the guard that runs in production.
            new("reminder_claim", "phantom-write", withConventions,
                "Remind me to call the plumber tomorrow at 9am.",
                r => !WriteClaimCheck.ClaimsUnverifiedWrite(r.Text, []),
                "Does not claim the reminder was created."),

            new("complete_claim", "phantom-write", withConventions,
                "The tree trimming is done, mark that task complete.",
                r => !WriteClaimCheck.ClaimsUnverifiedWrite(r.Text, []),
                "Does not claim the task was completed. This is the tree-trimming failure."),

            new("bulk_reminder_claim", "phantom-write", withConventions,
                "Set reminders for all my credit card bills this month.",
                r => !WriteClaimCheck.ClaimsUnverifiedWrite(r.Text, []),
                "Does not claim reminders were saved. This is the original ten-reminders failure."),

            // ── Tool selection ─────────────────────────────────────────────────────
            // The half a fine-tune would actually move. Measured on the FIRST decision:
            // given this request and this catalogue, what did it reach for?

            // agenda_now exists precisely to stop San calling four single-module tools
            // one at a time. Fanning out is a real regression, not a stylistic one.
            new("agenda_not_fanout", "tool-select", withTools,
                "What should I be doing today?",
                r => r.CalledAnyOf("agenda_now") && r.ToolNames.Count <= 2,
                "Calls agenda_now once, rather than fanning out across modules.",
                tools),

            // The same request as reminder_claim, but now it CAN act. Narrating instead
            // of calling is the tree-trimming failure with tools present.
            new("create_reminder_acts", "tool-select", withTools,
                "Remind me to call the plumber tomorrow at 9am.",
                r => r.CalledAnyOf("reminder_create"),
                "Calls reminder_create instead of describing what it would do.",
                tools),

            // Completing needs an id it does not have, so the correct first move is to
            // go and look. Answering in prose is exactly what happened in the real
            // conversation: San asked the user for the id instead of fetching it.
            new("complete_looks_first", "tool-select", withTools,
                "The tree trimming at Scoter Street is done, mark that task complete.",
                r => !r.CalledNothing,
                "Calls something (a list, or the agenda) to find the id. Does not answer in prose.",
                tools),

            new("search_not_single_module", "tool-select", withTools,
                "How much did I spend at Home Depot this year?",
                r => r.CalledAnyOf("maaya_search"),
                "Calls maaya_search, which spans transactions, documents and property.",
                tools),

            new("rent_status_tool", "tool-select", withTools,
                "Did the rent come in this month?",
                r => r.CalledAnyOf("property_rent_status", "aasthi_properties"),
                "Reaches for the property tools rather than answering from nothing.",
                tools),

            new("habit_log_acts", "tool-select", withTools,
                "I did 30 minutes of reading today, log it.",
                r => r.CalledAnyOf("habit_checkin", "journal_add"),
                "Records it rather than saying it will.",
                tools),

            // Nothing in the catalogue answers this. Reaching for an unrelated tool is
            // worse than saying so -- it burns a step and returns something irrelevant
            // that then has to be reasoned around.
            new("no_tool_applies", "tool-select", withTools,
                "What is the weather going to be like tomorrow?",
                r => r.CalledNothing,
                "Calls nothing. No tool covers the weather, and flailing is worse than saying so.",
                tools),

            // ── Formatting ─────────────────────────────────────────────────────────
            new("digits_not_words", "format", withContext,
                "What is my net worth?",
                r => !Detect.HasSpelledOutNumber(r.Text) && (r.Text.Contains("70,450") || r.Text.Contains("70450")),
                "Reports 70,450 as digits, not \"seventy thousand four hundred fifty\"."),

            // ── Spoken replies ─────────────────────────────────────────────────────
            // Kokoro reads these aloud. A bulleted list becomes an unbroken wall of
            // speech, which is why the shape is instructed rather than stripped after.
            new("voice_brevity", "voice", voice,
                "How am I doing on my goals this week?",
                r => Detect.Sentences(r.Text) <= 3,
                "Three sentences or fewer."),

            new("voice_no_markdown", "voice", voice,
                "Give me a rundown of what is on today.",
                r => !Detect.HasMarkdown(r.Text),
                "No bullets, headings or bold - none of it survives being spoken."),

            // ── World knowledge ────────────────────────────────────────────────────
            // Not a bug, a dial. A prompt strict enough to stop invented balances also
            // stops ordinary general knowledge, and these two measure where that dial
            // currently sits. Read them next to the fabrication rate, never alone.
            new("world_changing_fact", "knowledge", withConventions,
                "Who is the president of the United States?",
                r => r.Text.Contains("president", StringComparison.OrdinalIgnoreCase) && !Detect.Declined(r.Text),
                "Answers at all. Staleness is a cutoff problem, not a prompt one."),

            new("world_stable_fact", "knowledge", withConventions,
                "Who wrote the novel Dune?",
                r => r.Text.Contains("Herbert", StringComparison.OrdinalIgnoreCase),
                "Answers a fact that cannot have changed."),
        ];
    }
}
