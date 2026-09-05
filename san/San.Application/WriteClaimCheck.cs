namespace San.Application;

// Guards against San announcing work it never did.
//
// Asked to create ten reminders, the model once replied "I have saved 10 reminders"
// having called no tool at all. Nothing downstream could contradict it: a claim in
// prose looks exactly like a real one. The agent loop, though, knows every tool it
// actually executed -- so a completion claim can be checked against that record
// rather than taken on trust.
public static class WriteClaimCheck
{
    // A tool that changes state. Everything else (lists, searches, the agenda) is a
    // read, and a turn made only of reads can never justify "I have saved it". Matched
    // on the name because both catalogues -- the MCP gateway's ~41 tools and the
    // built-in registry -- share one verb convention, so a tool added later classifies
    // correctly without anyone remembering to update a list here.
    private static readonly string[] WriteVerbs =
        ["create", "add", "log", "set", "update", "delete", "remove", "complete", "checkin", "sync", "send"];

    public static bool IsWriteTool(string name)
    {
        var n = name.ToLowerInvariant();
        // "reminders_list" and "actions_pending" carry no verb; "action_complete" does.
        return WriteVerbs.Any(v => n.Contains(v, StringComparison.Ordinal));
    }

    // Deliberately narrow. It wants a first-person past-tense completion ("I have saved
    // the reminder", "10 reminders have been created"), never a capability or an offer
    // ("I can set that up", "shall I schedule it?") -- which is why no present tense
    // appears in any branch. A false positive costs one extra model step; missing a
    // real one costs the user their trust in every confirmation San gives.
    private static readonly System.Text.RegularExpressions.Regex ClaimPattern = new(
        // The modal lookbehind is what separates "I set the reminder" from "should I
        // set reminders for you?" - San asking permission is the single most common
        // sentence in this shape, and flagging it would nudge the model on every offer.
        @"\b(?:(?<!\b(?:should|shall|can|could|may|might|will|would|must|let|to)\s)i(?:'ve|\s+have)?\s+(?:now\s+|just\s+|successfully\s+|already\s+)*" +
        @"(?:created|added|saved|set(?:\s+up)?|scheduled|logged|booked|updated|deleted|removed|marked)" +
        @"|(?:has|have)\s+been\s+(?:created|added|saved|scheduled|logged|updated|deleted|set)" +
        @"|(?:reminders?|tasks?|events?|alerts?|actions?|goals?|habits?)\b[^.]{0,48}?\b(?:is|are)\s+(?:now\s+)?(?:set|created|saved|scheduled|added|logged))\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Announcing an action about to be taken, in a turn where it never was.
    //
    // Past tense alone was not enough. Asked to close out a job San replied "I will now
    // mark the task as complete", called nothing, and moved straight on to the next
    // topic -- and the user reasonably read that as done. "I will now" is not an offer,
    // it is a declaration, and the only thing between it and a lie is a tool call that
    // never came.
    //
    // The base-form verbs are deliberate ("mark", not "marked"): the past-tense branch
    // above already owns the completed claim.
    private static readonly System.Text.RegularExpressions.Regex IntentPattern = new(
        @"\b(?:i\s+will|i'll|i\s+am\s+going\s+to|i'm\s+going\s+to|let\s+me)\s+" +
        @"(?:now\s+|just\s+|quickly\s+|go\s+ahead\s+and\s+)*" +
        @"(?:mark|create|add|save|set(?:\s+up)?|schedule|log|book|update|delete|remove|complete)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.Compiled);

    // "I will add these once you tell me the timing" is a promise waiting on the user,
    // not a claim -- San is right to say it and right not to act yet. Flagging it would
    // nudge the model every time it correctly asks for something it needs first.
    private static readonly System.Text.RegularExpressions.Regex Conditional = new(
        @"\b(?:once|after|when|if|unless|before|until|provided|as\s+soon\s+as)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.Compiled);

    // True when the reply announces a write -- completed, or about to happen -- and no
    // write tool ran this turn.
    public static bool ClaimsUnverifiedWrite(string? content, IEnumerable<string> executedTools)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        if (executedTools.Any(IsWriteTool)) return false;   // it really did write something
        if (ClaimPattern.IsMatch(content)) return true;

        // Sentence by sentence, so a conditional in one clause cannot excuse a flat
        // declaration in another: "I need the id. I will now mark it complete." is
        // still a claim, made by the second sentence.
        foreach (var sentence in content.Split('.', '!', '?', '\n'))
        {
            if (!IntentPattern.IsMatch(sentence)) continue;
            if (Conditional.IsMatch(sentence)) continue;
            return true;
        }

        return false;
    }
}
