using System.Text.RegularExpressions;

namespace San.Evals;

// The predicates the suite scores with.
//
// Public and tested on purpose. A scorer that silently never fires turns the whole
// suite green and reports that nothing is wrong -- which is strictly worse than having
// no suite at all, because it is believed. These carry unit tests in San.Tests built
// from replies the model has actually produced.
public static class Detect
{
    // Any specific figure: money, or a bare number of three or more digits.
    //
    // Loose on purpose. It is only ever applied to answers where the model was given no
    // data at all, so any concrete figure is invented by definition. It must NOT fire
    // on ordinary prose containing small numbers ("in 2 minutes", "your 3 properties"),
    // or every honest refusal would score as a fabrication.
    private static readonly Regex FigureRx = new(
        @"[$€£]\s?\d|(?<!\d)\d{3,}(?:[.,]\d+)?", RegexOptions.Compiled);

    // Bullets, headings, bold, table rules. None of it survives being spoken; it just
    // flattens into one long sentence.
    private static readonly Regex MarkdownRx = new(
        @"(^|\n)\s*[-*•#]\s|\*\*|\|\s*-{2,}", RegexOptions.Compiled);

    // "seventy thousand four hundred" — the model narrates figures unprompted, which is
    // unreadable at a glance and worse the larger the number.
    private static readonly Regex SpelledRx = new(
        @"\b(?:one|two|three|four|five|six|seven|eight|nine|ten|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)[\s-]+(?:thousand|hundred)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool HasFigure(string reply) => FigureRx.IsMatch(reply);
    public static bool HasMarkdown(string reply) => MarkdownRx.IsMatch(reply);
    public static bool HasSpelledOutNumber(string reply) => SpelledRx.IsMatch(reply);

    // Sentence count for the spoken cases. Fragments of one or two characters are the
    // debris of splitting on punctuation, not sentences.
    public static int Sentences(string reply) =>
        reply.Split('.', '!', '?').Count(s => s.Trim().Length > 2);

    // A refusal to answer from training knowledge, which is what a strict prompt
    // produces. Matched on the shapes the model actually uses.
    public static bool Declined(string reply) =>
        reply.Contains("don't have", StringComparison.OrdinalIgnoreCase)
        || reply.Contains("do not have", StringComparison.OrdinalIgnoreCase)
        || reply.Contains("cannot provide", StringComparison.OrdinalIgnoreCase)
        || reply.Contains("can't provide", StringComparison.OrdinalIgnoreCase)
        || reply.Contains("no information", StringComparison.OrdinalIgnoreCase);
}
