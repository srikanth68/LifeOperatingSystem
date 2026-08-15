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
    // ("I can set that up", "shall I schedule it?") -- which is why no present or future
    // tense appears in any branch. A false positive costs one extra model step; missing
    // a real one costs the user their trust in every confirmation San gives.
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

    // True when the reply announces a completed write and no write tool ran this turn.
    public static bool ClaimsUnverifiedWrite(string? content, IEnumerable<string> executedTools)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        if (executedTools.Any(IsWriteTool)) return false;   // it really did write something
        return ClaimPattern.IsMatch(content);
    }
}
