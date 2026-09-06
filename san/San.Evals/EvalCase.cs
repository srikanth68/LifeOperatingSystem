namespace San.Evals;

// One thing the model is expected to do, and a way to tell whether it did.
//
// `Passes` returns true for CORRECT behaviour. It is deliberately a plain predicate
// over the reply text rather than a model-graded judgement: an LLM judge would put the
// thing under test on both sides of the scale, and on a 4B that is not a measurement,
// it is two coin flips agreeing.
public record EvalCase(
    string Name,
    string Category,
    string SystemPrompt,
    string UserMessage,
    Func<string, bool> Passes,
    string Expectation);

// How one case behaved across N runs.
//
// A RATE, never a verdict. At temperature 0.7 a single sample says almost nothing --
// and this project exists partly because a single sample once said the opposite of the
// truth: a prompt rewrite looked clean on one run and turned out to fabricate a bank
// balance in six runs out of ten.
public record CaseResult(
    string Name,
    string Category,
    int Runs,
    int Passed,
    string Expectation,
    List<string> FailureSamples)
{
    public double Rate => Runs == 0 ? 0 : (double)Passed / Runs;

    // Shown in the scoreboard. Not a threshold anyone should tune against -- it is a
    // reading aid, and the number is what matters.
    public string Mark => Rate switch
    {
        >= 0.95 => "ok",
        >= 0.70 => "weak",
        _ => "BAD",
    };
}
