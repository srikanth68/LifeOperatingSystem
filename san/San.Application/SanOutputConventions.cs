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

    // Appended INSTEAD of nothing extra when the turn arrived by voice — the user spoke,
    // and Kokoro will read the reply back aloud.
    //
    // Without this San writes for a screen and the result gets spoken at you: headings,
    // bullet lists and a paragraph of context, flattened by CleanForSpeech into an
    // unbroken wall. Stripping the markup was never the problem — the reply was the
    // wrong SHAPE, and only the model can fix that, so it has to be told.
    //
    // Costs nothing on typed turns: it is only added when the client says the turn was
    // spoken, which keeps the prompt budget where it was for normal chat.
    //
    // Note it does NOT relax the digits rule above. TTS engines read "$70,450" correctly
    // and spelling it out would only make San's own text worse if it were ever shown.
    public const string Voice =
        "THIS TURN WAS SPOKEN, AND YOUR REPLY WILL BE READ ALOUD:\n" +
        "- Answer in one to three sentences. Say the single most useful thing and stop.\n" +
        "- No lists, headings, bullets, tables or markdown of any kind — none of it survives " +
        "being spoken, it just becomes one long flat sentence.\n" +
        "- Write the way you would say it out loud, in plain connected prose.\n" +
        "- Lead with the answer. Do not restate the question or narrate what you are about to do.\n" +
        "- If the full answer is genuinely long, give the headline and offer the rest: " +
        "\"...do you want the detail?\"";
}
