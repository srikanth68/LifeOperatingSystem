using System.Text.Json;
using San.Evals;

// Measures what the guardrails cannot: whether the model itself is getting better or
// worse.
//
// Every automated test in this repo exercises deterministic C#. Not one sends anything
// to Gemma, which means a prompt edit, a quantisation change, a model swap or a
// fine-tune all land with no way to tell what they did. That gap produced a real
// mistake: a prompt rewrite was called a win on the strength of ONE sample, and
// measuring it properly showed it had gone from never inventing a bank balance to
// inventing one in six runs out of ten.
//
// So this is a console tool, not a test project. It needs a live model, it reports
// rates rather than pass/fail, and it must never run as part of the normal gate.
//
//   dotnet run --project san/San.Evals -- --runs 10 --out baseline.json
//   dotnet run --project san/San.Evals -- --compare baseline.json

var arg = (string name, string? fallback) =>
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
};

var url = arg("--url", Environment.GetEnvironmentVariable("LLM_BASE_URL"));
if (string.IsNullOrWhiteSpace(url))
{
    Console.Error.WriteLine("No model endpoint. Pass --url http://host:8080 or set LLM_BASE_URL.");
    return 2;
}

var runs = int.TryParse(arg("--runs", "10"), out var n) ? Math.Clamp(n, 1, 200) : 10;
var temp = double.TryParse(arg("--temp", "0.7"), out var t) ? t : 0.7;
var filter = arg("--filter", null);
var outPath = arg("--out", null);
var comparePath = arg("--compare", null);

var promptPath = arg("--prompt", Path.Combine(AppContext.BaseDirectory, "prompts", "chat.txt"));
if (!File.Exists(promptPath))
{
    Console.Error.WriteLine($"System prompt not found: {promptPath}");
    Console.Error.WriteLine("Pass --prompt <file> pointing at the prompt you actually run.");
    return 2;
}

var chatPrompt = await File.ReadAllTextAsync(promptPath);
var client = new ModelClient(url!, temp, enableThinking: false);

string model;
try { model = await client.ModelNameAsync(); }
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not reach {url}: {ex.Message}");
    return 2;
}

var cases = Cases.Build(chatPrompt)
    .Where(c => filter is null || c.Category.Equals(filter, StringComparison.OrdinalIgnoreCase))
    .ToList();

Console.WriteLine();
Console.WriteLine($"model   {model}");
Console.WriteLine($"prompt  {Path.GetFileName(promptPath)} ({chatPrompt.Length} chars)");
Console.WriteLine($"runs    {runs} per case at temp {temp}   ({cases.Count * runs} calls)");
Console.WriteLine();

var results = new List<CaseResult>();

foreach (var c in cases)
{
    var passed = 0;
    var failures = new List<string>();

    for (var i = 0; i < runs; i++)
    {
        ModelReply reply;
        try { reply = await client.AskAsync(c.SystemPrompt, c.UserMessage, c.Tools); }
        catch (Exception ex)
        {
            // A transport failure is not a model failure, and silently scoring it as
            // one would make an unreachable box look like a broken model.
            Console.Error.WriteLine($"  ! {c.Name} run {i + 1}: {ex.Message}");
            continue;
        }

        if (c.Passes(reply)) passed++;
        else if (failures.Count < 3) failures.Add(Flatten(reply));

        Console.Write($"\r  {c.Category}/{c.Name}  {i + 1}/{runs}   ");
    }

    var result = new CaseResult(c.Name, c.Category, runs, passed, c.Expectation, failures);
    results.Add(result);
    Console.WriteLine($"\r  {c.Category,-14} {c.Name,-26} {passed,3}/{runs}  {result.Mark}      ");
}

Console.WriteLine();
Console.WriteLine("BY CATEGORY");
foreach (var g in results.GroupBy(r => r.Category))
{
    var p = g.Sum(x => x.Passed);
    var total = g.Sum(x => x.Runs);
    Console.WriteLine($"  {g.Key,-14} {p,4}/{total,-4} {(total == 0 ? 0 : 100.0 * p / total),5:0.0}%");
}

// Failures are printed verbatim. A rate says something regressed; only the text says
// what the model actually did, and that is what a prompt or training fix is aimed at.
var bad = results.Where(r => r.FailureSamples.Count > 0).ToList();
if (bad.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("WHAT FAILURE LOOKED LIKE");
    foreach (var r in bad)
    {
        Console.WriteLine($"  {r.Name}  (expected: {r.Expectation})");
        foreach (var s in r.FailureSamples) Console.WriteLine($"    - {s}");
    }
}

if (comparePath is not null && File.Exists(comparePath))
{
    var before = JsonSerializer.Deserialize<List<CaseResult>>(await File.ReadAllTextAsync(comparePath)) ?? [];
    Console.WriteLine();
    Console.WriteLine($"AGAINST {Path.GetFileName(comparePath)}");

    var moved = false;
    foreach (var now in results)
    {
        var then = before.FirstOrDefault(b => b.Name == now.Name);
        if (then is null) { Console.WriteLine($"  {now.Name,-26} new"); moved = true; continue; }

        var delta = now.Rate - then.Rate;
        // A couple of runs either way at temp 0.7 is noise, not a signal. Only movement
        // worth acting on is printed, or every comparison reads as a change.
        if (Math.Abs(delta) < 0.15) continue;

        moved = true;
        Console.WriteLine($"  {now.Name,-26} {then.Rate:P0} -> {now.Rate:P0}  {(delta > 0 ? "better" : "WORSE")}");
    }

    if (!moved) Console.WriteLine("  nothing moved beyond noise.");
}

if (outPath is not null)
{
    await File.WriteAllTextAsync(outPath,
        JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine();
    Console.WriteLine($"baseline written to {outPath}");
}

Console.WriteLine();
return 0;

// A tool-selection failure often has empty prose -- the whole failure IS the call list,
// or its absence -- so both are printed. "called nothing" is the single most
// informative thing a failing tool case can say.
static string Flatten(ModelReply reply)
{
    var calls = reply.ToolNames.Count == 0 ? "called nothing" : "called " + string.Join(", ", reply.ToolNames);
    var text = string.Join(" ", reply.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)).Trim();
    if (text.Length > 140) text = text[..140] + "…";
    return string.IsNullOrEmpty(text) ? $"[{calls}]" : $"[{calls}] {text}";
}
