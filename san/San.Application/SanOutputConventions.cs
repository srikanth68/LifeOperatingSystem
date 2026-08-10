namespace San.Application;

// Formatting rules for anything San writes for a human to read — chat replies and
// the Telegram lines the workers produce.
//
// Kept OUT of the editable prompts on purpose. The chat, audit, and triage prompts
// are all user-editable and stored in Settings, so a rule written into their default
// text stops applying the moment the user edits their own version. This block is
// appended by the caller every turn instead, so it holds regardless.
//
// Appended LAST as well: gemma-4-E4B follows instructions near the end of a long
// system prompt noticeably better than ones buried above several KB of module
// snapshot.
public static class SanOutputConventions
{
    // The model narrates figures in words unprompted — a balance of 70450 comes back
    // as "seventy thousand four hundred and fifty", which is unreadable at a glance
    // and worse the larger the number.
    public const string Text =
        "FORMATTING:\n" +
        "- Write every number as digits, never spelled out. \"$70,450\" — never " +
        "\"seventy thousand four hundred and fifty\". This applies to money, counts, " +
        "measurements, durations, and percentages alike.\n" +
        "- Use a thousands separator on figures of four digits or more, and keep the " +
        "currency symbol on money.\n" +
        "- Write percentages as 12%, not \"twelve percent\".\n" +
        "- Round to at most two decimal places.";
}
