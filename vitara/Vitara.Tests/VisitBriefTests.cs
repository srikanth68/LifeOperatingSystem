using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// The sheet taken to an appointment, and the things it must refuse to become.
//
// Most of this file is about restraint rather than output. A brief like this is one
// careless sentence away from being an AI diagnosis, and the specific failure to guard
// is not the dramatic one — nobody is going to make it say "you have cancer". It is
// the quiet one: a sheet that looks complete, read by someone who then treats the
// absence of a finding as the absence of a problem.
public class VisitBriefTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    private static Finding Running(string metric, string type, int days, string severity = "notable") => new()
    {
        Key = $"{type}:{metric}", Type = type, Metric = metric, Severity = severity,
        Summary = $"{metric} did something for {days} days.",
        FirstDetectedLocal = Today.AddDays(-(days - 1)),
        LastDetectedLocal = Today,
    };

    private static VisitBrief.Result Build(
        IReadOnlyList<Finding>? findings = null,
        IReadOnlyList<Measurement>? labs = null,
        IReadOnlyList<LabPanel>? panels = null,
        IReadOnlyList<Baseline>? baselines = null,
        string? sex = null) =>
        VisitBrief.Build(
            findings ?? [], labs ?? [], panels ?? [],
            ReferenceRanges.Seed, baselines ?? [], sex, 41, Today);

    // ── The refusals ────────────────────────────────────────────────────────────

    [Fact]
    public void It_always_says_what_it_cannot_see()
    {
        var brief = Build();

        Assert.NotEmpty(brief.NotLookedAt);
        Assert.Contains(brief.NotLookedAt, x => x.Contains("symptoms"));
        Assert.Contains(brief.NotLookedAt, x => x.Contains("medications"));
        Assert.Contains(brief.NotLookedAt, x => x.Contains("family history"));
    }

    [Fact]
    public void A_clean_sheet_does_not_say_nothing_is_wrong()
    {
        // The dangerous sentence. Everything measured being fine is a statement about
        // what was measured, and this has to be the one place it cannot be rounded off.
        var brief = Build();

        Assert.Contains("not the same as nothing being wrong", brief.Verdict);
    }

    [Fact]
    public void The_disclaimer_refuses_both_diagnosis_and_treatment()
    {
        var brief = Build();

        Assert.Contains("not a diagnosis", brief.Disclaimer);
        Assert.Contains("not advice about treatment", brief.Disclaimer);
        Assert.Contains("question for a clinician", brief.Disclaimer);
    }

    [Fact]
    public void Unsettled_measurements_are_declared_so_their_absence_means_nothing()
    {
        var baselines = new List<Baseline>
        {
            new() { Metric = "resting_hr", IsValid = true },
            new() { Metric = "hrv_rmssd", IsValid = true },
            new() { Metric = "systolic_bp", IsValid = false },
            new() { Metric = "glucose", IsValid = false },
        };

        var brief = Build(baselines: baselines);

        Assert.Contains("2 do not yet", brief.Coverage);
        Assert.Contains("their absence from this sheet means nothing", brief.Coverage);
    }

    [Fact]
    public void No_item_names_a_condition()
    {
        // Templated questions, never generated. A model writing these would eventually
        // write one that reads as a diagnosis.
        var brief = Build(findings:
        [
            Running(MetricKeys.RestingHeartRate, FindingTypes.Drift, 40),
            Running(MetricKeys.Tsh, FindingTypes.LabAnchor, 2),
            Running("vitals", FindingTypes.EarlyIllness, 3, "high"),
        ]);

        var words = new[] { "diagnos", "disease", "disorder", "syndrome", "you have", "probably have", "suffer" };
        foreach (var text in brief.Bring.Select(b => b.Ask).Concat(brief.Questions).Where(x => x is not null))
            Assert.DoesNotContain(words, w => text!.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    // ── What it does produce ────────────────────────────────────────────────────

    [Fact]
    public void Longest_running_comes_first_because_duration_is_what_earns_the_appointment()
    {
        var brief = Build(findings:
        [
            Running(MetricKeys.HrvRmssd, FindingTypes.Deviation, 2),
            Running(MetricKeys.RestingHeartRate, FindingTypes.Drift, 40),
        ]);

        Assert.Equal(2, brief.Bring.Count);
        Assert.Contains("40 days", brief.Bring[0].Since);
    }

    [Fact]
    public void A_finding_seen_once_says_so_rather_than_claiming_a_day()
    {
        var brief = Build(findings: [Running(MetricKeys.HrvRmssd, FindingTypes.Deviation, 1)]);

        Assert.Equal("first seen today", brief.Bring[0].Since);
    }

    [Fact]
    public void A_long_drift_raises_a_question_and_a_short_one_does_not()
    {
        var slow = Build(findings: [Running(MetricKeys.RestingHeartRate, FindingTypes.Drift, 40)]);
        var brief = Build(findings: [Running(MetricKeys.RestingHeartRate, FindingTypes.Drift, 5)]);

        Assert.NotNull(slow.Bring[0].Ask);
        Assert.Null(brief.Bring[0].Ask);
    }

    [Fact]
    public void An_out_of_range_lab_is_brought_with_its_draw_date()
    {
        var panel = new LabPanel { DrawnOnLocal = new DateOnly(2026, 9, 30), LabName = "Quest" };
        var labs = new List<Measurement>
        {
            new() { Metric = MetricKeys.Ldl, Value = 164, Unit = "mg/dL", LabPanelId = panel.Id, Day = panel.DrawnOnLocal },
            new() { Metric = MetricKeys.Hba1c, Value = 5.2, Unit = "%", LabPanelId = panel.Id, Day = panel.DrawnOnLocal },
        };

        var brief = Build(labs: labs, panels: [panel]);

        var item = Assert.Single(brief.Bring);
        Assert.Equal("LDL cholesterol", item.Topic);
        Assert.Contains("above the usual range", item.What);
        Assert.Contains("30 Sep 2026", item.Since);
        // The in-range HbA1c is not brought — a sheet of everything is a sheet nobody reads.
        Assert.DoesNotContain(brief.Bring, b => b.Topic == "HbA1c");
    }

    [Fact]
    public void A_womans_hdl_is_read_against_her_own_range()
    {
        // 45 is inside the male range and below the female one. Handing her the wrong
        // row is the quiet failure this guards.
        var panel = new LabPanel { DrawnOnLocal = Today.AddDays(-3) };
        var labs = new List<Measurement>
        {
            new() { Metric = MetricKeys.Hdl, Value = 45, Unit = "mg/dL", LabPanelId = panel.Id, Day = panel.DrawnOnLocal },
        };

        Assert.Single(Build(labs: labs, panels: [panel], sex: "female").Bring);
        Assert.Empty(Build(labs: labs, panels: [panel], sex: "male").Bring);
    }

    [Fact]
    public void With_no_blood_work_at_all_it_asks_whether_any_is_worth_doing()
    {
        Assert.Contains(Build().Questions, q => q.Contains("no blood work recorded"));
    }

    [Fact]
    public void It_always_ends_by_asking_what_is_not_being_measured()
    {
        // The question the sheet cannot answer for itself, and the most valuable one on it.
        Assert.Contains(Build().Questions, q => q.Contains("not measuring"));
    }

    [Fact]
    public void Questions_are_not_repeated()
    {
        var brief = Build(findings:
        [
            Running(MetricKeys.Tsh, FindingTypes.LabAnchor, 2),
            Running(MetricKeys.Tsh, FindingTypes.LabAnchor, 3),
        ]);

        Assert.Equal(brief.Questions.Distinct().Count(), brief.Questions.Count);
    }
}
