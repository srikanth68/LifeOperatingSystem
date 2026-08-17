using System.Text.RegularExpressions;

namespace San.Application;

// Decides whether a distilled line is worth storing as a long-term memory.
//
// Asked "add a reminder for tomorrow morning to go to USPS at 10am", San replied
// "I've set a reminder for you" and called nothing. The reason was in its own context
// block: memory recall had matched the word "reminder" and returned eight lines like
//
//     Reminder set for tomorrow morning at 8am for cricket
//     reminder set for today at 4pm to go to the mall
//     The user wants a reminder created for tomorrow, Wednesday, July 23...
//
// Eight worked examples in which the right answer to a reminder request is the
// SENTENCE "reminder set for X". The model completed the pattern it was shown. Even
// the re-prompt could not recover it, because the examples were still there.
//
// None of those should ever have been memories. A reminder already lives in the
// reminders table, where it can be completed, edited and expired. Copying it into the
// brain creates a second record that can do none of those things and never goes away
// -- and unlike the table, the copy is fed back to the model as context.
//
// Two rules, both deterministic. The extraction prompt asks the model to avoid these
// as well, but a 4B model classifying "user asked for a reminder" as a `decision` is
// exactly what happened for weeks, so the filter cannot rely on being obeyed.
public static class MemoryWorthKeeping
{
    // A record of one action performed in a module that owns it. Note this matches the
    // ACT of setting a reminder, not the topic -- see DurableSignal.
    private static readonly Regex ActionRecord = new(
        @"\b(?:" +
        @"(?:set|sets|setting|created?|creates|add(?:ed|s)?|schedul(?:e|ed|es)|saved?)\s+" +
        @"(?:up\s+)?(?:a|an|the|some)?\s*(?:new\s+)?(?:reminder|alert|task|calendar\s+event|action\s+item)s?" +
        @"|(?:reminder|alert|task|calendar\s+event|action\s+item)s?\s+" +
        @"(?:has\s+been\s+|have\s+been\s+|was\s+|were\s+|is\s+|are\s+)?(?:set|created|added|scheduled|saved)" +
        @"|user\s+(?:wants?|wanted|asked\s+for|requested|decided\s+to\s+(?:set|create|add))\b[^.]{0,40}?" +
        @"\b(?:reminder|alert|task|event)s?" +
        @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // What the user is LIKE, as opposed to what was done once. "User prefers reminders
    // in the morning" is worth keeping forever and mentions reminders, so this has to
    // be tested before ActionRecord or the good memory goes out with the bad.
    private static readonly Regex DurableSignal = new(
        @"\b(?:prefers?|prefer|likes?|dislikes?|hates?|always|usually|typically|never|" +
        @"every\s+(?:day|morning|evening|week|month)|habit\s+of|goal\s+is|works?\s+at|lives?\s+in)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "2 PM today" was true the day it was written and has been read as current on
    // every one of the forty days since. A memory whose only time reference is relative
    // cannot be interpreted later, so it is not durable by definition.
    private static readonly Regex RelativeDate = new(
        @"\b(?:today|tomorrow|yesterday|tonight|this\s+(?:morning|afternoon|evening|week|month)|" +
        @"next\s+week|later\s+today)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AbsoluteDate = new(
        @"(?:\b\d{4}-\d{2}-\d{2}\b|\b(?:january|february|march|april|may|june|july|august|" +
        @"september|october|november|december)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // reason is non-null exactly when the answer is false, so the worker can log why.
    public static bool Keep(string? text, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(text)) { reason = "empty"; return false; }

        if (DurableSignal.IsMatch(text)) return true;

        if (ActionRecord.IsMatch(text))
        {
            reason = "records an action already stored in the module that owns it";
            return false;
        }

        if (RelativeDate.IsMatch(text) && !AbsoluteDate.IsMatch(text))
        {
            reason = "relative date with nothing to anchor it";
            return false;
        }

        return true;
    }
}
