using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// Each draw annotated with the daily data before it (Vitara ADR 0020).
public class LabContextTests
{
    private static readonly DateOnly Draw = new(2026, 9, 1);
    private static readonly DateOnly Earlier = new(2026, 3, 1);

    private static Observation Lab(Guid panel, string metric, double value, DateOnly day) => new()
    {
        Metric = metric, Value = value, ObservedDateLocal = day, ObservedAtLocal = day.ToDateTime(new TimeOnly(8, 0)),
        LabPanelId = panel, Tier = Tiers.Sparse,
    };

    private static IEnumerable<Observation> Daily(string metric, double value, DateOnly before, int days, int everyDays = 1) =>
        Enumerable.Range(1, days).Where(i => i % everyDays == 0).Select(i => new Observation
        {
            Metric = metric, Value = value, ObservedDateLocal = before.AddDays(-i),
            ObservedAtLocal = before.AddDays(-i).ToDateTime(new TimeOnly(7, 0)),
        });

    [Fact]
    public void AnHba1cIsReadAgainstThreeMonths_AndAnLdlAgainstOne()
    {
        var panel = Guid.NewGuid();
        var obs = new List<Observation> { Lab(panel, MetricKeys.Hba1c, 5.4, Draw), Lab(panel, MetricKeys.Ldl, 110, Draw) };

        var windows = LabContext.Windows(Draw, [MetricKeys.Hba1c, MetricKeys.Ldl], obs, [], [], []);

        Assert.Equal(90, windows.Single(w => w.Analytes.Contains(MetricKeys.Hba1c)).Days);
        Assert.Equal(30, windows.Single(w => w.Analytes.Contains(MetricKeys.Ldl)).Days);
        Assert.Equal(Draw.AddDays(-1), windows[0].To);   // the draw day itself is not "before"
    }

    [Fact]
    public void TenDaysOfRingDataInThirtyIsARefusal()
    {
        var panel = Guid.NewGuid();
        var obs = new List<Observation> { Lab(panel, MetricKeys.Ldl, 110, Draw) }
            .Concat(Daily(MetricKeys.Steps, 8000, Draw, 30, everyDays: 3))
            .ToList();

        var steps = LabContext.Windows(Draw, [MetricKeys.Ldl], obs, [], [], [])
            .Single().Metrics.Single(m => m.Metric == MetricKeys.Steps);

        Assert.False(steps.Summarised);
        Assert.Contains("10 of 30 days", steps.Refusal);
    }

    [Fact]
    public void ADrawIsSetBesideThePreviousDraw_InPlainCoOccurrence()
    {
        var now = Guid.NewGuid();
        var before = Guid.NewGuid();
        var panels = new List<LabPanel> { new() { Id = now, DrawnOnLocal = Draw }, new() { Id = before, DrawnOnLocal = Earlier } };
        var obs = new List<Observation> { Lab(now, MetricKeys.Ldl, 104, Draw), Lab(before, MetricKeys.Ldl, 128, Earlier) }
            .Concat(Daily(MetricKeys.Steps, 9100, Draw, 30))
            .Concat(Daily(MetricKeys.Steps, 6300, Earlier, 30))
            .ToList();

        var context = LabContext.ForPanel(now, panels, obs, [], [], [])!;

        Assert.Equal(before, context.PreviousPanelId);
        Assert.Contains(context.Sentences, s => s.Contains("median steps were 9,100, up from 6,300 before the previous one"));
    }

    [Fact]
    public void NoSentenceClaimsACause()
    {
        var now = Guid.NewGuid();
        var before = Guid.NewGuid();
        var panels = new List<LabPanel> { new() { Id = now, DrawnOnLocal = Draw }, new() { Id = before, DrawnOnLocal = Earlier } };
        var obs = new List<Observation> { Lab(now, MetricKeys.Hba1c, 5.2, Draw), Lab(before, MetricKeys.Hba1c, 5.6, Earlier) };
        foreach (var m in LabContext.DailyMetrics)
            obs.AddRange(Daily(m, 50, Draw, 90).Concat(Daily(m, 40, Earlier, 90)));

        var context = LabContext.ForPanel(now, panels, obs, [], [], [])!;

        Assert.NotEmpty(context.Sentences);
        foreach (var sentence in context.Sentences)
            foreach (var word in LabContext.CausalWords)
                Assert.DoesNotContain(word, sentence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterventionsAndFindingsDuringTheWindowAreListed()
    {
        var panel = Guid.NewGuid();
        var obs = new List<Observation> { Lab(panel, MetricKeys.Ldl, 110, Draw) };
        var started = new List<Intervention> { new() { Name = "More fibre", StartedOnLocal = Draw.AddDays(-20) } };
        var findings = new List<Finding>
        {
            new() { Summary = "Sleep debt building", FirstDetectedLocal = Draw.AddDays(-10), LastDetectedLocal = Draw.AddDays(-5) },
            new() { Summary = "Long ago", FirstDetectedLocal = Draw.AddDays(-200), LastDetectedLocal = Draw.AddDays(-190), ResolvedLocal = Draw.AddDays(-190) },
        };

        var window = LabContext.Windows(Draw, [MetricKeys.Ldl], obs, [], findings, started).Single();

        Assert.Single(window.Interventions, i => i.StartsWith("More fibre"));
        Assert.Equal(["Sleep debt building"], window.Findings);
    }
}
