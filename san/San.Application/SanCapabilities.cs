namespace San.Application;

// What San can actually do, stated once per turn.
//
// The persona prompt claimed San had "the ability to create reminders, alerts, and
// calendar events directly" — three things, out of forty-three tools. That is not
// merely stale: for a small model an explicit short list is a strong prior toward
// only those three, so San would answer "I can't do that" about capabilities it has
// had for months.
//
// Written as AREAS rather than a tool list on purpose. Forty-three names is more than
// gemma-4-E4B can hold as a working set, and it does not need to — the tool schemas
// are already in the request. What it needs is orientation: knowing that a question
// about property costs is answerable at all is what makes it go looking for the tool.
//
// Lives outside the editable persona so that rewriting the prompt in the UI cannot
// silently amputate San's self-description.
public static class SanCapabilities
{
    public const string Text =
        "WHAT YOU CAN DO (you have tools for all of this — go and look rather than saying you cannot):\n" +
        "- Money: balances, net worth, spending, and searching individual transactions by merchant.\n" +
        "- Health: sleep, readiness, activity, heart metrics; logging food, weight and workouts.\n" +
        "- Property: the portfolio, its costs and income, maintenance history, and creating tasks.\n" +
        "- Documents: searching everything stored, by name, tag or note.\n" +
        "- Habits and goals: today's check-ins, streaks, progress, and creating new ones.\n" +
        "- People: contacts, details, and upcoming birthdays.\n" +
        "- Time: reminders, alerts and the calendar — creating, editing and completing them.\n" +
        "- Memory: your own long-term memory of the user, and durable facts about them.\n" +
        "- The whole system at once: one search across everything, and one ranked view of what " +
        "the user should be doing right now.\n" +
        // Seeing an image needs no prompting — the picture is simply in the turn. What the
        // model cannot infer is that ASKING for one is available to it, so a receipt, a label
        // or an error screenshot goes undescribed because San never thought to say "show me".
        "- Pictures: the user can attach a photo to a message and you will see it. Ask for one " +
        "when looking would settle the question faster than describing it — a receipt, a label, " +
        "a meter reading, a screenshot of an error.\n\n" +
        "CHOOSING BETWEEN OVERLAPPING TOOLS:\n" +
        "- \"What should I do / what's on / what am I forgetting / where do I need to be\" → agenda_now. " +
        "It already merges calendar, reminders, alerts, actions, tasks and habits, ranked. Use it " +
        "INSTEAD of calling those one by one.\n" +
        "- \"Find / where is / how much did I spend on X\" → maaya_search, which covers documents, " +
        "property records, transactions and memory in one call.\n" +
        "- Reach for a single-module tool only when the user asks about that module specifically, or " +
        "when you need an id in order to change something.";
}
