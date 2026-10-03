using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Vitara.API.Controllers;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// Blood work: the one tier that cannot be handled like the others.
//
// Everything else here earns a personal normal from ninety days of readings. Labs
// arrive twice a year, and the honest consequence is that a lab value is read against
// a printed range and the previous draw -- never fitted, never z-scored, never given
// a baseline built from three points.
//
// The tests that matter most are the ones about restraint: a range is not a diagnosis,
// a lab finding is never "high" severity however far outside it sits, and an analyte
// nobody enumerated is kept rather than silently dropped.
public class LabTests
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly DateOnly Today = new(2026, 10, 3);

    // ── Reference range selection ───────────────────────────────────────────────

    [Fact]
    public void A_sex_specific_range_beats_the_generic_one()
    {
        var ranges = new List<ReferenceRange>
        {
            new() { Metric = MetricKeys.Hdl, Low = 40, Unit = "mg/dL", Sex = "male" },
            new() { Metric = MetricKeys.Hdl, Low = 50, Unit = "mg/dL", Sex = "female" },
        };

        Assert.Equal(50, ReferenceRanges.For(ranges, MetricKeys.Hdl, "female")!.Low);
        Assert.Equal(40, ReferenceRanges.For(ranges, MetricKeys.Hdl, "male")!.Low);
    }

    [Fact]
    public void A_sex_specific_range_is_not_applied_to_someone_else()
    {
        // The failure this guards: sorting first and winning. A woman handed the male
        // HDL range is told 45 is fine when her own range says it is not.
        var ranges = new List<ReferenceRange> { new() { Metric = MetricKeys.Hdl, Low = 40, Sex = "male" } };

        Assert.Null(ReferenceRanges.For(ranges, MetricKeys.Hdl, "female"));
    }

    [Fact]
    public void The_users_own_lab_beats_a_generic_range()
    {
        var ranges = new List<ReferenceRange>
        {
            new() { Metric = MetricKeys.Tsh, Low = 0.4, High = 4.0 },
            new() { Metric = MetricKeys.Tsh, Low = 0.5, High = 4.5, LabName = "Quest" },
        };

        Assert.Equal(4.5, ReferenceRanges.For(ranges, MetricKeys.Tsh, labName: "Quest")!.High);
        Assert.Equal(4.0, ReferenceRanges.For(ranges, MetricKeys.Tsh)!.High);
    }

    [Fact]
    public void An_analyte_with_no_range_is_unknown_rather_than_normal()
    {
        // "No range recorded" and "inside the range" must never collapse into the same
        // answer; one of them is reassurance nobody checked.
        Assert.Equal(ReferenceRanges.Standing.Unknown, ReferenceRanges.Where(5, null));
    }

    [Theory]
    [InlineData(3.0, ReferenceRanges.Standing.Below)]
    [InlineData(5.0, ReferenceRanges.Standing.Within)]
    [InlineData(9.9, ReferenceRanges.Standing.Above)]
    public void A_value_is_placed_against_its_band(double value, ReferenceRanges.Standing expected)
    {
        var range = new ReferenceRange { Metric = MetricKeys.Tsh, Low = 4.0, High = 9.0 };
        Assert.Equal(expected, ReferenceRanges.Where(value, range));
    }

    [Fact]
    public void Every_seeded_range_names_what_it_is_and_carries_a_unit()
    {
        Assert.NotEmpty(ReferenceRanges.Seed);
        Assert.All(ReferenceRanges.Seed, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Metric));
            Assert.False(string.IsNullOrWhiteSpace(r.Unit));
            Assert.False(string.IsNullOrWhiteSpace(r.Notes));     // the caveat travels with the range
            Assert.True(r.Low is not null || r.High is not null); // a range bounded at neither end is not one
        });
    }

    // ── The detector ────────────────────────────────────────────────────────────

    private static readonly ReferenceRange Tsh = new() { Metric = MetricKeys.Tsh, Low = 0.4, High = 4.0, Unit = "mIU/L" };

    [Fact]
    public void Inside_the_range_and_unchanged_says_nothing()
    {
        var finding = FindingDetectors.LabAnchor(
            MetricKeys.Tsh, "TSH", 2.1, Today.AddDays(-10), 2.0, Today.AddDays(-200), Tsh, Today);

        Assert.Null(finding);
    }

    [Fact]
    public void Outside_the_range_is_notable_and_never_high()
    {
        // However far outside it sits. "High" severity means act today, and a blood
        // result is a conversation with a doctor rather than an emergency this app is
        // competent to declare.
        var finding = FindingDetectors.LabAnchor(
            MetricKeys.Tsh, "TSH", 19.0, Today.AddDays(-3), null, null, Tsh, Today)!;

        Assert.Equal("notable", finding.Severity);
        Assert.Equal("high", finding.Direction);
        Assert.Contains("above the usual range", finding.Summary);
    }

    [Fact]
    public void Every_lab_finding_says_it_is_not_a_diagnosis()
    {
        var finding = FindingDetectors.LabAnchor(
            MetricKeys.Tsh, "TSH", 0.1, Today.AddDays(-3), null, null, Tsh, Today)!;

        Assert.Contains("not a diagnosis", finding.Summary);
        Assert.Contains("next appointment", finding.Summary);
    }

    [Fact]
    public void A_move_is_measured_against_the_width_of_the_range()
    {
        // 2.0 to 3.9 is inside the range at both ends and crosses most of its width
        // (the band is 0.4-4.0, so half of it is 1.8). Twenty per cent would be noise
        // on a TSH and a different person's risk on an LDL, which is why a percentage
        // is the wrong scale and the band is the right one.
        var finding = FindingDetectors.LabAnchor(
            MetricKeys.Tsh, "TSH", 3.9, Today.AddDays(-3), 2.0, Today.AddDays(-190), Tsh, Today)!;

        Assert.Equal("info", finding.Severity);
        Assert.Equal("rising", finding.Direction);
        Assert.Contains("moved from 2 to 3.9", finding.Summary);

        // And the same journey stopping short of half the band is not news.
        Assert.Null(FindingDetectors.LabAnchor(
            MetricKeys.Tsh, "TSH", 3.5, Today.AddDays(-3), 2.0, Today.AddDays(-190), Tsh, Today));
    }

    [Fact]
    public void A_small_move_inside_the_range_is_not_news()
    {
        Assert.Null(FindingDetectors.LabAnchor(
            MetricKeys.Tsh, "TSH", 2.3, Today.AddDays(-3), 2.0, Today.AddDays(-190), Tsh, Today));
    }

    [Fact]
    public void A_draw_from_two_years_ago_does_not_open_a_finding_this_morning()
    {
        Assert.Null(FindingDetectors.LabAnchor(
            MetricKeys.Tsh, "TSH", 19.0, Today.AddDays(-800), null, null, Tsh, Today));
    }

    [Fact]
    public void Without_a_range_only_a_move_can_speak()
    {
        // No range means no opinion about whether the value is acceptable — only about
        // whether it changed.
        Assert.Null(FindingDetectors.LabAnchor(
            "ferritin", "Ferritin", 400, Today.AddDays(-3), null, null, null, Today));

        var moved = FindingDetectors.LabAnchor(
            "ferritin", "Ferritin", 400, Today.AddDays(-3), 100, Today.AddDays(-200), null, Today)!;

        Assert.Equal("info", moved.Severity);
        Assert.Equal(0.5, moved.Confidence);
    }

    // ── Through a whole run ─────────────────────────────────────────────────────

    private static Observation LabReading(string metric, double value, DateOnly day, Guid panel) => new()
    {
        Metric = metric, Value = value, Unit = "mIU/L",
        ObservedDateLocal = day, ObservedAtLocal = day.ToDateTime(new TimeOnly(8, 0)),
        Tier = Tiers.Sparse, Source = "lab", LabPanelId = panel,
    };

    [Fact]
    public void A_run_compares_the_last_draw_with_the_one_before_it()
    {
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();

        var input = new FindingRunInputs(
            [
                LabReading(MetricKeys.Tsh, 2.0, Today.AddDays(-200), older),
                LabReading(MetricKeys.Tsh, 6.5, Today.AddDays(-5), newer),
            ],
            [], [], Today,
            ReferenceRangeRows: [Tsh]);

        var findings = FindingRun.Detect(input);
        var lab = Assert.Single(findings, f => f.Type == FindingTypes.LabAnchor);

        Assert.Equal("notable", lab.Severity);
        Assert.Contains("6.5", lab.Summary);
    }

    [Fact]
    public void A_lab_metric_is_never_given_a_z_score_finding()
    {
        // The sparse tier has no personal baseline by design. If one ever appears, a
        // deviation finding on two readings a year would be a confident number built
        // on nothing.
        var panel = Guid.NewGuid();
        var input = new FindingRunInputs(
            [LabReading(MetricKeys.Tsh, 6.5, Today.AddDays(-5), panel)],
            [], [], Today,
            ReferenceRangeRows: [Tsh]);

        Assert.DoesNotContain(FindingRun.Detect(input), f => f.Type == FindingTypes.Deviation);
    }

    // ── The API ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_panel_stores_every_analyte_against_one_draw()
    {
        var repo = new FakeRepo();
        var ctrl = new LabsController(repo);

        var ok = Assert.IsType<OkObjectResult>(await ctrl.Post(new LabsController.LabPanelInput(
            "2026-09-30", "Quest", "fasting",
            [
                new(MetricKeys.Hba1c, 5.4, null, null),
                new(MetricKeys.Ldl, 96, null, null),
                new(MetricKeys.Hdl, 58, null, null),
            ])));

        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;

        Assert.Equal(3, json.GetProperty("stored").GetInt32());
        Assert.Single(repo.LabPanelData);
        Assert.Equal(3, repo.MeasurementData.Count);
        // One draw, one id, shared by all three.
        Assert.Single(repo.MeasurementData.Select(m => m.LabPanelId).Distinct());
        Assert.All(repo.MeasurementData, m => Assert.Equal("sparse", m.Tier));
    }

    [Fact]
    public async Task An_analyte_nobody_enumerated_is_kept_and_reported()
    {
        // Somebody's panel will always carry one more thing than this catalogue does.
        // Dropping it silently teaches them not to bother entering any of it.
        var ctrl = new LabsController(new FakeRepo());

        var ok = Assert.IsType<OkObjectResult>(await ctrl.Post(new LabsController.LabPanelInput(
            "2026-09-30", null, null, [new("homocysteine", 9.1, "umol/L", null)])));

        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;

        Assert.Equal(1, json.GetProperty("stored").GetInt32());
        Assert.Equal("homocysteine", json.GetProperty("unrecognised")[0].GetString());
        Assert.Contains("not analytes this system knows about", json.GetProperty("note").GetString()!);
    }

    [Fact]
    public async Task A_draw_dated_in_the_future_is_refused()
    {
        // Not pedantry: a future date puts the reading beyond every window that looks
        // for it, which is invisible rather than wrong.
        var ctrl = new LabsController(new FakeRepo());

        var result = await ctrl.Post(new LabsController.LabPanelInput(
            DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"), null, null,
            [new(MetricKeys.Hba1c, 5.4, null, null)]));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task An_empty_panel_is_refused()
    {
        var ctrl = new LabsController(new FakeRepo());
        Assert.IsType<BadRequestObjectResult>(await ctrl.Post(new LabsController.LabPanelInput(null, null, null, [])));
    }

    [Fact]
    public async Task Deleting_a_draw_takes_its_readings_with_it()
    {
        var repo = new FakeRepo();
        var ctrl = new LabsController(repo);

        await ctrl.Post(new LabsController.LabPanelInput("2026-09-30", null, null,
            [new(MetricKeys.Hba1c, 5.4, null, null), new(MetricKeys.Ldl, 96, null, null)]));

        var id = repo.LabPanelData[0].Id;
        Assert.IsType<NoContentResult>(await ctrl.Delete(id));

        Assert.Empty(repo.LabPanelData);
        Assert.Empty(repo.MeasurementData);
    }

    [Fact]
    public async Task A_reading_is_returned_with_its_range_and_its_change()
    {
        var repo = new FakeRepo();
        repo.RangeData.AddRange(ReferenceRanges.Seed);
        var ctrl = new LabsController(repo);

        await ctrl.Post(new LabsController.LabPanelInput("2026-03-01", null, null, [new(MetricKeys.Ldl, 96, null, null)]));
        await ctrl.Post(new LabsController.LabPanelInput("2026-09-30", null, null, [new(MetricKeys.Ldl, 131, null, null)]));

        var ok = Assert.IsType<OkObjectResult>(await ctrl.Get());
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;

        var newest = json[0];                       // newest first
        var ldl = newest.GetProperty("results")[0];

        Assert.Equal("above", ldl.GetProperty("standing").GetString());
        Assert.Equal(96, ldl.GetProperty("previous").GetDouble());
        Assert.Equal(35, ldl.GetProperty("change").GetDouble());
        Assert.Contains("above the usual range", ldl.GetProperty("standingText").GetString()!);
    }
}
