namespace San.Application;

// What San may and may not say when the question is about a body.
//
// Appended alongside the output conventions and for the same reason: the chat prompt
// is user-editable and stored in Settings, so a rule written into its default text
// stops applying the moment it is edited. This one in particular must not be
// accidentally editable — it is the difference between an assistant that helps
// somebody prepare for an appointment and one that quietly practises medicine.
//
// The failure mode worth designing against is not the obvious one. A local Gemma is
// not going to volunteer a cancer diagnosis. What it WILL do, asked "should I be
// worried about my resting heart rate", is produce a fluent, confident, plausible
// paragraph that sounds like it came from somebody qualified — containing a likely
// cause, a reassurance, and a suggestion. Every part of that is invented, and the
// user has no way to tell which parts came from their data and which from the model's
// priors. Hence the rule: say what the measurements say, say what is not known, and
// hand the question on.
//
// Vitara already computes everything factual here. health_findings, health_baselines
// and the visit brief are all deterministic, which means the honest answer to almost
// any health question is a tool call followed by a quotation.
public static class MedicalBoundary
{
    public const string Text =
        "HEALTH QUESTIONS:\n" +
        "- You are not a doctor and must not act like one. Never name a condition the user " +
        "might have, never suggest a diagnosis, never recommend or adjust a medication, dose " +
        "or supplement, and never tell them a reading is nothing to worry about.\n" +
        "- Answer from their measurements, not from medical knowledge. Call health_findings, " +
        "health_baselines or vitara_health and report what those actually say. If a tool has " +
        "not been called, you do not know.\n" +
        "- A reading outside a reference range is a question for a clinician, not an answer. " +
        "Say what it is, how long it has been that way, and that it is worth raising.\n" +
        "- Say what you cannot see: you have no symptoms, no medications, no family history and " +
        "no examination. Never let an absence of findings stand as reassurance — \"nothing is " +
        "flagged\" is a statement about what was measured and must be said that way.\n" +
        "- If they ask what to DO about a health reading, the answer is to raise it with a " +
        "clinician, and you can help them prepare for that conversation.\n" +
        "- Urgent symptoms — chest pain, trouble breathing, stroke signs, a thought of self-harm " +
        "— are not yours to triage. Say plainly that it needs emergency care now, and stop.";

    // Only when it is relevant. Health is a minority of turns and this is ~230 tokens;
    // paying it on "what did I spend at the hardware shop" buys nothing. Matching on
    // the question AND the recent reply, because "is that bad?" is a health question
    // when the previous turn was about a heart rate.
    private static readonly string[] Cues =
    [
        "health", "doctor", "gp", "symptom", "pain", "sick", "ill", "illness", "fever",
        "blood", "lab", "cholesterol", "ldl", "hdl", "hba1c", "thyroid", "tsh", "glucose",
        "diabet", "pressure", "bp", "heart", "hrv", "pulse", "sleep", "apnea", "apnoea",
        "medication", "medicine", "dose", "supplement", "diagnos", "worried", "worry",
        "normal range", "test result", "results", "appointment", "clinic", "hospital",
    ];

    public static bool Applies(params string?[] recentText) =>
        recentText.Any(t => !string.IsNullOrWhiteSpace(t)
                            && Cues.Any(c => t!.Contains(c, StringComparison.OrdinalIgnoreCase)));
}
