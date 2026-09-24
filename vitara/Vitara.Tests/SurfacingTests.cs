using Vitara.Insight.Health;
using Vitara.Domain.Entities;

namespace Vitara.Tests;

// Which findings lead, out of everything currently true.
//
// The rules here are judgement, not statistics, so they are the ones most likely to be
// changed by someone who does not know why they were chosen. Each test states the why.
public class SurfacingTests
{
    private static readonly DateOnly Today = new(2026, 9, 24);

    private static Finding F(string key, string severity, int daysRunning, string type = "deviation") => new()
    {
        Key = key,
        Type = type,
        Metric = key,
        Severity = severity,
        Summary = key,
        FirstDetectedLocal = Today.AddDays(-(daysRunning - 1)),
        LastDetectedLocal = Today,
    };

    [Fact]
    public void Nothing_active_surfaces_nothing_and_says_nothing()
    {
        var result = Surfacing.Choose([]);

        Assert.Empty(result.Surfaced);
        Assert.Empty(result.Standing);
        Assert.Equal("", result.Note);
    }

    [Fact]
    public void Under_the_cap_everything_leads()
    {
        var result = Surfacing.Choose([F("a", "notable", 1), F("b", "info", 1)], cap: 3);

        Assert.Equal(2, result.Surfaced.Count);
        Assert.Empty(result.Standing);
        Assert.Equal("", result.Note);
    }

    [Fact]
    public void Over_the_cap_the_rest_stand_rather_than_disappear()
    {
        var findings = new[]
        {
            F("a", "notable", 1), F("b", "notable", 2), F("c", "info", 1), F("d", "info", 2), F("e", "info", 3),
        };

        var result = Surfacing.Choose(findings, cap: 3);

        Assert.Equal(3, result.Surfaced.Count);
        Assert.Equal(2, result.Standing.Count);
        // Nothing is dropped: the two lists together are still everything that is true.
        Assert.Equal(findings.Length, result.Surfaced.Count + result.Standing.Count);
        Assert.Contains("2 more", result.Note);
    }

    [Fact]
    public void Severity_leads_before_anything_else()
    {
        var result = Surfacing.Choose([F("quiet", "info", 1), F("loud", "notable", 30)], cap: 1);

        Assert.Equal("loud", Assert.Single(result.Surfaced).Key);
    }

    // The point of the whole file. A finding on its fortieth day has been said thirty-nine
    // times; at equal severity the new one is the news.
    [Fact]
    public void New_beats_long_running_at_the_same_severity()
    {
        var result = Surfacing.Choose([F("old", "notable", 40), F("new", "notable", 1)], cap: 1);

        Assert.Equal("new", Assert.Single(result.Surfaced).Key);
    }

    // A cap exists to stop noise crowding out signal. Applying it to "high" would make it
    // do the opposite on the one morning it matters.
    [Fact]
    public void Serious_findings_are_never_held_back_even_past_the_cap()
    {
        var findings = new[]
        {
            F("ill", "high", 2), F("bp", "high", 4), F("lab", "high", 1), F("drift", "high", 9),
            F("minor", "info", 1),
        };

        var result = Surfacing.Choose(findings, cap: 3);

        Assert.Equal(4, result.Surfaced.Count);
        Assert.All(result.Surfaced, f => Assert.Equal("high", f.Severity));
        Assert.Equal("minor", Assert.Single(result.Standing).Key);
    }

    [Fact]
    public void Serious_findings_take_the_slots_before_the_rest_fill_them()
    {
        var result = Surfacing.Choose([F("ill", "high", 3), F("a", "notable", 1), F("b", "notable", 2)], cap: 2);

        Assert.Equal(["ill", "a"], result.Surfaced.Select(f => f.Key));
        Assert.Equal(["b"], result.Standing.Select(f => f.Key));
    }

    // Two runs over the same findings must agree, or the tab and San contradict each other.
    [Fact]
    public void Ties_are_broken_deterministically()
    {
        var findings = new[] { F("zulu", "info", 1), F("alpha", "info", 1), F("mike", "info", 1) };

        var first = Surfacing.Choose(findings, cap: 2);
        var second = Surfacing.Choose(findings.Reverse().ToArray(), cap: 2);

        Assert.Equal(first.Surfaced.Select(f => f.Key), second.Surfaced.Select(f => f.Key));
        Assert.Equal(["alpha", "mike"], first.Surfaced.Select(f => f.Key));
    }

    [Fact]
    public void One_held_back_is_said_in_the_singular()
    {
        var result = Surfacing.Choose([F("a", "info", 1), F("b", "info", 2)], cap: 1);

        Assert.StartsWith("One more finding", result.Note);
    }

    [Fact]
    public void Days_running_counts_the_first_day_as_day_one()
    {
        Assert.Equal(1, Surfacing.DaysRunning(F("a", "info", 1)));
        Assert.Equal(7, Surfacing.DaysRunning(F("b", "info", 7)));
    }
}
