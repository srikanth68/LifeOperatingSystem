using System.Text.RegularExpressions;

namespace Maaya.Time.Tests;

// A guard that reads the source, not the behaviour.
//
// The wrong-day bug is not one mistake in one place. It is a way of writing "today" that
// looks completely correct, compiles everywhere, and is wrong for four hours of every
// evening -- so it recurs the moment somebody writes a new controller from memory. About
// forty instances accumulated across seven modules before anyone noticed, because every
// one of them produces a plausible number.
//
// A behavioural test cannot catch the next one: it would have to be written against a
// controller that does not exist yet. A test over the source can, and it fails with the
// file and line, which is the only form of review that scales across every module.
//
// WHAT IS BANNED, and what to write instead:
//
//   DateOnly.FromDateTime(DateTime.UtcNow)   MaayaClock.Today
//   DateTime.UtcNow.Date / .Year / .Month    MaayaClock.Today (.Year, .Month ...)
//   DateTime.Now / DateTime.Today            MaayaClock.Now / MaayaClock.Today
//   .ToLocalTime()                           MaayaClock.FromUtc(...)
//   TimeZoneInfo.Local                       MaayaClock.Zone
//
// DateTime.UtcNow ITSELF is not banned. It is the right way to stamp an instant -- a
// CreatedAt, a token expiry, an elapsed-time measurement. Only turning that instant into
// a calendar day or an hour of the day is the mistake.
public class NoWrongDayTests
{
    private static readonly (Regex Pattern, string Use)[] Banned =
    [
        (new Regex(@"DateOnly\.FromDateTime\(\s*DateTime\.(UtcNow|Now|Today)\s*\)"), "MaayaClock.Today"),
        (new Regex(@"DateTime\.UtcNow\.(Date|Year|Month|Day|DayOfWeek|Hour|TimeOfDay)\b"), "MaayaClock.Today / MaayaClock.Now"),
        (new Regex(@"\bDateTime\.Now\b"), "MaayaClock.Now"),
        (new Regex(@"\bDateTime\.Today\b"), "MaayaClock.Today"),
        (new Regex(@"\.ToLocalTime\(\)"), "MaayaClock.FromUtc(...)"),
        (new Regex(@"\bTimeZoneInfo\.Local\b"), "MaayaClock.Zone"),
    ];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(Directory.Exists(Path.Combine(dir.FullName, "shared"))
                                    && Directory.Exists(Path.Combine(dir.FullName, "scripts"))))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repo root from " + AppContext.BaseDirectory);
    }

    private static bool InScope(string path)
    {
        var p = path.Replace('\\', '/');

        // Not production code, or the one place allowed to touch the primitives.
        if (p.Contains("/obj/") || p.Contains("/bin/") || p.Contains("/node_modules/")) return false;
        if (p.Contains("/shared/Maaya.Time/")) return false;
        if (Regex.IsMatch(p, @"/[^/]*Tests?/")) return false;

        return true;
    }

    [Fact]
    public void NoModuleDerivesADayFromTheWrongClock()
    {
        var offenders = new List<string>();
        var root = RepoRoot();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(InScope))
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();

                // Comments explain the mistake by naming it; that is not the mistake.
                if (line.StartsWith("//")) continue;

                foreach (var (pattern, use) in Banned)
                    if (pattern.IsMatch(lines[i]))
                        offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}  {line.Trim()}   ->   use {use}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A calendar day or wall-clock hour is being derived from the wrong clock. UTC is four or five " +
            "hours ahead of New York, so this is wrong from 7-8pm local onward:\n  " + string.Join("\n  ", offenders));
    }

    // The web app has the same mistake in a different spelling. `new Date().toISOString()` is
    // the UTC moment, so slicing a date off it gives today in London: from 8pm in New York a
    // form's "today" default and a "last 30 days" window both end a day in the future, and from
    // anywhere east of Greenwich a local-midnight Date reduces to the PREVIOUS day.
    private static readonly (Regex Pattern, string Use)[] BannedWeb =
    [
        (new Regex(@"toISOString\(\)\s*\.(slice|substring|split)\("), "todayInTz() / dayInTz() / addDays() from services/timezone"),
        (new Regex(@"new Date\(\)\.(getHours|getMinutes|getDate|getDay|getMonth|getFullYear)\("), "zonedNow().getX() from services/timezone"),
    ];

    [Fact]
    public void NoPageDerivesADayFromTheBrowsersClock()
    {
        var offenders = new List<string>();
        var root = RepoRoot();
        var web = Path.Combine(root, "vault", "frontend", "src");

        foreach (var file in Directory.EnumerateFiles(web, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".ts") || f.EndsWith(".tsx")))
        {
            // The helpers are the one place allowed to touch the primitives.
            if (file.Replace('\\', '/').EndsWith("/services/timezone.ts")) continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var t = lines[i].TrimStart();
                if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*")) continue;

                foreach (var (pattern, use) in BannedWeb)
                    if (pattern.IsMatch(lines[i]))
                        offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}  {t.Trim()}   ->   use {use}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A day or hour is being derived from the browser's own clock instead of the configured zone:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheWebGuardActuallyFindsTheThingItGuardsAgainst()
    {
        var samples = new[]
        {
            "const today = new Date().toISOString().slice(0, 10);",
            "end: new Date().toISOString().split('T')[0],",
            "const hour = new Date().getHours();",
            "const year = new Date().getFullYear();",
        };

        foreach (var sample in samples)
            Assert.Contains(BannedWeb, b => b.Pattern.IsMatch(sample));

        // And the replacements must not be flagged, or the guard is disabled by its own fix.
        foreach (var fine in new[] { "const today = todayInTz();", "const hour = zonedNow().getHours();" })
            Assert.DoesNotContain(BannedWeb, b => b.Pattern.IsMatch(fine));
    }

    [Fact]
    public void TheGuardActuallyFindsTheThingItGuardsAgainst()
    {
        // A guard that matches nothing passes forever and protects nothing. Pin that each
        // banned pattern really does fire on the code it is meant to catch.
        var samples = new[]
        {
            "var today = DateOnly.FromDateTime(DateTime.UtcNow);",
            "var end = DateTime.UtcNow.Date;",
            "var month = DateTime.UtcNow.Month;",
            "var now = DateTime.Now;",
            "var d = DateTime.Today;",
            "var local = stamp.ToLocalTime();",
            "return TimeZoneInfo.Local;",
        };

        foreach (var sample in samples)
            Assert.Contains(Banned, b => b.Pattern.IsMatch(sample));
    }

    [Fact]
    public void AnInstantStampIsNotFlagged()
    {
        // The other direction: DateTime.UtcNow for a timestamp is correct and must stay
        // legal, or the guard gets disabled the first time somebody sets a CreatedAt.
        var legitimate = new[]
        {
            "CreatedAt = DateTime.UtcNow,",
            "var expires = DateTime.UtcNow.AddMinutes(30);",
            "var elapsed = DateTime.UtcNow - started;",
            "var cutoff = DateTime.UtcNow.AddHours(-24);",
        };

        foreach (var sample in legitimate)
            Assert.DoesNotContain(Banned, b => b.Pattern.IsMatch(sample));
    }

    [Fact]
    public void TheGuardCanFindTheRepo()
    {
        // If the root lookup silently landed somewhere empty, the main test would pass on
        // zero files. Require that it actually sees a meaningful amount of the codebase.
        var count = Directory.EnumerateFiles(RepoRoot(), "*.cs", SearchOption.AllDirectories).Count(InScope);

        Assert.True(count > 200, $"only {count} source files in scope — the repo root lookup is probably wrong");
    }
}
