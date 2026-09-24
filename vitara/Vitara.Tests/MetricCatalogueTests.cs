using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Controllers;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// The catalogue is the promise that nothing is hidden: every metric the system knows
// about has a row, whether or not it has ever held a reading. A metric missing from here
// is invisible in the UI, and invisible looks exactly like "not supported".
public class MetricCatalogueTests
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly DateOnly Today = LocalTime.Today;

    [Fact]
    public void EveryKnownMetricHasACatalogueRow()
    {
        var keys = typeof(MetricKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        var missing = keys.Where(k => MetricCatalogue.Find(k) is null).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryRowCanBeShownToAPersonWithoutFurtherExplanation()
    {
        foreach (var m in MetricCatalogue.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Label), $"{m.Key} has no label");
            Assert.False(string.IsNullOrWhiteSpace(m.What), $"{m.Key} has no description");
            Assert.Contains(m.Group, MetricCatalogue.Groups);
            Assert.Contains(m.Source, new[] { "ring", "phone", "manual", "lab", "computed" });
        }
    }

    [Fact]
    public void TheDerivedValuesAreListedAsMetricsToo()
    {
        // They are computed rather than measured, and that distinction belongs in the
        // row -- not in whether the row exists.
        foreach (var key in new[]
        {
            MetricCatalogue.SleepDebtMinutes, MetricCatalogue.AcwrActiveCalories,
            MetricCatalogue.Bmi, MetricCatalogue.WaistToHeight,
        })
        {
            var info = MetricCatalogue.Find(key);
            Assert.NotNull(info);
            Assert.True(info!.Computed);
        }
    }

    // ── The endpoint ────────────────────────────────────────────────────────────

    private static async Task<JsonElement> MetricsAsync(FakeRepo repo)
    {
        var ok = Assert.IsType<OkObjectResult>(await new HealthIntelligenceController(repo).Metrics());
        return JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;
    }

    private static JsonElement Row(JsonElement payload, string key) =>
        payload.GetProperty("metrics").EnumerateArray().Single(m => m.GetProperty("key").GetString() == key);

    [Fact]
    public async Task AMetricWithNoReadingsStillHasARow_SayingSoAndSayingWhatWouldFillIt()
    {
        var payload = await MetricsAsync(new FakeRepo());

        Assert.Equal(MetricCatalogue.All.Count, payload.GetProperty("metrics").GetArrayLength());

        var labs = Row(payload, MetricKeys.Hba1c);
        Assert.Equal("no_data", labs.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, labs.GetProperty("latest").ValueKind);
        Assert.Equal(0, labs.GetProperty("readings").GetInt32());
        Assert.Contains("blood test", labs.GetProperty("fillWith").GetString());
    }

    [Fact]
    public async Task AZeroIsAReadingAndNotAGap()
    {
        // The whole point of the state field: 0 steps is a day you did not move, which
        // is data. Showing it as empty would be a lie about what is known.
        var repo = new FakeRepo();
        repo.ObservationData.Add(new Observation
        {
            Metric = MetricKeys.Steps, Value = 0, ObservedDateLocal = Today,
            ObservedAtLocal = Today.ToDateTime(new TimeOnly(23, 0)),
        });

        var steps = Row(await MetricsAsync(repo), MetricKeys.Steps);

        Assert.Equal("current", steps.GetProperty("state").GetString());
        Assert.Equal(0, steps.GetProperty("latest").GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task AReadingThatStoppedArrivingReadsAsStale_NotAsCurrent()
    {
        var repo = new FakeRepo();
        repo.ObservationData.Add(new Observation
        {
            Metric = MetricKeys.RestingHeartRate, Value = 54, ObservedDateLocal = Today.AddDays(-9),
            ObservedAtLocal = Today.AddDays(-9).ToDateTime(new TimeOnly(7, 0)),
        });

        var row = Row(await MetricsAsync(repo), MetricKeys.RestingHeartRate);

        Assert.Equal("stale", row.GetProperty("state").GetString());
        Assert.Equal(9, row.GetProperty("latest").GetProperty("daysAgo").GetInt32());
    }

    [Fact]
    public async Task AThinBaselineSaysHowManyMoreReadingsItNeeds()
    {
        var repo = new FakeRepo();
        repo.BaselineData.Add(new Baseline
        {
            Metric = MetricKeys.HrvRmssd, BaselineSignature = "", ComputedOnLocal = Today,
            Median = 44, P25 = 39, P75 = 49, N = 9, IsValid = false, WindowDays = 60,
        });

        var row = Row(await MetricsAsync(repo), MetricKeys.HrvRmssd);
        var baseline = row.GetProperty("baseline");

        Assert.Equal("learning", baseline.GetProperty("state").GetString());
        Assert.Equal(9, baseline.GetProperty("n").GetInt32());
        Assert.Equal(12, baseline.GetProperty("needs").GetInt32());   // 21 - 9
    }

    [Fact]
    public async Task TheBestSupportedContextBucketIsTheOneShown()
    {
        // Blood pressure carries one baseline per position; an arbitrary first would put
        // a two-reading bucket in front of a sixty-reading one.
        var repo = new FakeRepo();
        repo.BaselineData.Add(new Baseline
        {
            Metric = MetricKeys.SystolicBp, BaselineSignature = "position=standing|timeOfDay=evening",
            ComputedOnLocal = Today, Median = 132, N = 3, IsValid = false, WindowDays = 60,
        });
        repo.BaselineData.Add(new Baseline
        {
            Metric = MetricKeys.SystolicBp, BaselineSignature = "position=seated|timeOfDay=morning",
            ComputedOnLocal = Today, Median = 121, N = 40, IsValid = true, WindowDays = 60,
        });

        var baseline = Row(await MetricsAsync(repo), MetricKeys.SystolicBp).GetProperty("baseline");

        Assert.Equal("position=seated|timeOfDay=morning", baseline.GetProperty("signature").GetString());
        Assert.Equal("ready", baseline.GetProperty("state").GetString());
    }
}
