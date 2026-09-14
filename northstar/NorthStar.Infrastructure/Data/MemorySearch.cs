using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;

namespace NorthStar.Infrastructure.Data;

// How long-term memory is searched: the full-text index, the query built from what the
// user said, and the ranking applied to what it finds.
//
// Recall used to split the message on spaces and OR every word, stopwords included, then
// order by bm25 alone. "what is my car" matched every memory containing "is" and "my"; a
// plural never found its singular; and the importance, age and reinforcement stored on
// every memory played no part in which ones came back. San was handed whatever happened
// to share filler words with the question.
//
// This stays lexical -- "my car" still will not find a memory that only says "Honda".
// That needs embeddings, a separate step with its own model. What this fixes is the
// part keyword search can do properly and was not.
public static partial class MemorySearch
{
    // porter stems both the index and the query ("meetings" and "meeting" are one term);
    // unicode61 underneath keeps non-English text tokenised the way it was.
    public const string FtsTableSql =
        "CREATE VIRTUAL TABLE IF NOT EXISTS MemoryFts USING fts5(" +
        "Content, Tags, content='Memories', content_rowid='rowid', tokenize='porter unicode61')";

    private const string TriggersSql = """
        CREATE TRIGGER IF NOT EXISTS Memories_ai AFTER INSERT ON Memories BEGIN
            INSERT INTO MemoryFts(rowid, Content, Tags) VALUES (new.rowid, new.Content, new.Tags);
        END;
        CREATE TRIGGER IF NOT EXISTS Memories_ad AFTER DELETE ON Memories BEGIN
            INSERT INTO MemoryFts(MemoryFts, rowid, Content, Tags) VALUES ('delete', old.rowid, old.Content, old.Tags);
        END;
        CREATE TRIGGER IF NOT EXISTS Memories_au AFTER UPDATE OF Content, Tags ON Memories BEGIN
            INSERT INTO MemoryFts(MemoryFts, rowid, Content, Tags) VALUES ('delete', old.rowid, old.Content, old.Tags);
            INSERT INTO MemoryFts(rowid, Content, Tags) VALUES (new.rowid, new.Content, new.Tags);
        END;
        """;

    // Idempotent, and migrates an index built before stemming. A tokenizer cannot be
    // changed on an existing FTS5 table, so an old one is dropped and rebuilt from the
    // Memories table it indexes -- the memories themselves are never touched.
    public static async Task EnsureSchemaAsync(DbConnection conn, CancellationToken ct = default)
    {
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        string? existing;
        await using (var probe = conn.CreateCommand())
        {
            probe.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'MemoryFts'";
            existing = await probe.ExecuteScalarAsync(ct) as string;
        }

        var outdated = existing is not null && !existing.Contains("porter", StringComparison.OrdinalIgnoreCase);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            (outdated
                ? "DROP TRIGGER IF EXISTS Memories_ai; DROP TRIGGER IF EXISTS Memories_ad; " +
                  "DROP TRIGGER IF EXISTS Memories_au; DROP TABLE IF EXISTS MemoryFts; "
                : "")
            + FtsTableSql + "; "
            + TriggersSql
            // A fresh or rebuilt index starts empty; 'rebuild' fills it from Memories.
            + (existing is null || outdated ? " INSERT INTO MemoryFts(MemoryFts) VALUES ('rebuild');" : "");
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── The query ────────────────────────────────────────────────────────────

    // Words that carry no topic. Includes the ways people ask a memory question ("do you
    // remember", "tell me"), because those are exactly the words that used to match
    // every memory about anything.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "if", "of", "to", "in", "on", "at", "for", "from", "by",
        "with", "about", "as", "into", "over", "up", "out", "is", "am", "are", "was", "were", "be",
        "been", "being", "do", "does", "did", "doing", "have", "has", "had", "i", "me", "my", "mine",
        "myself", "we", "us", "our", "you", "your", "yours", "he", "him", "his", "she", "her", "it",
        "its", "they", "them", "their", "this", "that", "these", "those", "what", "which", "who",
        "whom", "whose", "when", "where", "why", "how", "can", "could", "would", "should", "will",
        "shall", "may", "might", "must", "not", "no", "yes", "so", "just", "any", "some", "all",
        "there", "here", "then", "than", "too", "very", "again", "also", "please", "tell", "know",
        "remember", "recall", "think", "san", "hey", "hi", "hello", "ok", "okay", "thanks", "thank",
        "s", "t", "d", "m", "ll", "re", "ve", "don", "didn", "doesn", "isn", "wasn", "get", "got",
    };

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRegex();

    // The topic words of a message, lowercased, in order, without repeats. Letters and
    // digits only, so nothing the user types can ever be read as FTS5 query syntax.
    public static IReadOnlyList<string> Keywords(string? query) =>
        WordRegex().Matches(query ?? "")
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => w.Length >= 2 && !StopWords.Contains(w))
            .Distinct()
            .Take(12)
            .ToList();

    // Each word as a stemmed term, and words of four letters or more also as a prefix, so
    // "hond" finds "Honda". Null when the message has no topic words at all -- recalling
    // nothing beats recalling everything that shares an "is".
    public static string? BuildMatch(string? query)
    {
        var words = Keywords(query);
        if (words.Count == 0) return null;
        return string.Join(" OR ", words.Select(w => w.Length >= 4 ? $"\"{w}\" OR \"{w}\"*" : $"\"{w}\""));
    }

    // ── The ranking ──────────────────────────────────────────────────────────

    public const double RecencyHalfLifeDays = 120;

    // bm25 is negative, lower is better, and unbounded. Each score becomes its share of
    // the best match's (the best is 1), so it can be blended with the other signals.
    //
    // Deliberately not stretched min-to-max: two memories matching almost equally well
    // would then land at 1 and 0, and a hair's difference in text match would outweigh
    // everything importance and recency are there to decide.
    public static double[] NormaliseBm25(IReadOnlyList<double> bm25)
    {
        if (bm25.Count == 0) return [];
        var best = bm25.Min();
        return best >= 0
            ? bm25.Select(_ => 1.0).ToArray()
            : bm25.Select(b => Math.Clamp(b / best, 0.0, 1.0)).ToArray();
    }

    // Relevance leads; importance, recency and reinforcement reorder memories that match
    // about equally well, and cannot lift a poor match over a good one on their own.
    // Reinforcement is capped low: a memory recalled often rises a little, but must not
    // become the answer to everything because it was the answer before.
    public static double Score(double relevance, int importance, DateTime createdAt, int accessCount, DateTime now)
    {
        var ageDays = Math.Max(0, (now - createdAt).TotalDays);
        var recency = Math.Pow(0.5, ageDays / RecencyHalfLifeDays);
        var importanceNorm = (Math.Clamp(importance, 1, 5) - 1) / 4.0;
        var reinforcement = Math.Min(1.0, Math.Log(1 + Math.Max(0, accessCount)) / Math.Log(21));
        return 0.60 * relevance + 0.20 * importanceNorm + 0.12 * recency + 0.08 * reinforcement;
    }

    // For collapsing the same memory saved twice: case, spacing and trailing punctuation
    // are not a difference worth showing San two lines for.
    public static string Normalise(string content) =>
        string.Join(' ', WordRegex().Matches(content.ToLowerInvariant()).Select(m => m.Value));
}
