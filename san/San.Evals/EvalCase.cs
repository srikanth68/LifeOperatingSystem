using San.Application.Interfaces;

namespace San.Evals;

// What came back from one turn: the prose, and whatever tools it decided to call.
//
// Both matter, and for different questions. "Did it invent a figure" is about the
// text; "did it act or merely narrate" is about the calls -- and the tree-trimming
// failure was precisely a turn with confident prose and an empty call list.
public record ModelReply(string Text, IReadOnlyList<string> ToolNames)
{
    public bool CalledNothing => ToolNames.Count == 0;
    public bool CalledAnyOf(params string[] names) =>
        ToolNames.Any(n => names.Contains(n, StringComparer.OrdinalIgnoreCase));
}

// One thing the model is expected to do, and a way to tell whether it did.
//
// `Tools` is null for the text-only cases, which deliberately offer nothing: a model
// that cannot call anything and still says it saved a reminder has told a plain lie,
// and that is worth measuring on its own. The tool cases hand over a fixed catalogue
// and ask a different question -- given that it CAN act, does it.
//
// `Passes` returns true for CORRECT behaviour, and is a plain predicate rather than a
// model-graded judgement: an LLM judge would put the thing under test on both sides of
// the scale, and on a 4B that is two coin flips agreeing, not a measurement.
public record EvalCase(
    string Name,
    string Category,
    string SystemPrompt,
    string UserMessage,
    Func<ModelReply, bool> Passes,
    string Expectation,
    List<ToolDefinition>? Tools = null);

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

    // Shown in the scoreboard. Not a threshold to tune against -- it is a reading aid,
    // and the number is what matters.
    public string Mark => Rate switch
    {
        >= 0.95 => "ok",
        >= 0.70 => "weak",
        _ => "BAD",
    };
}
