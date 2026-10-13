using Microsoft.AspNetCore.Mvc;
using Vitara.API.Controllers;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// The hold's finding and the acceptance endpoint. The calculator's half is pinned in
// RegimeHoldTests; this is what makes a hold visible and how a person ends one.
public class RegimeHoldWiringTests
{
    private static readonly DateOnly Today = new(2026, 9, 8);

    private static RegimeHold Held(DateOnly changePoint, DateOnly? released = null) => new()
    {
        Metric = MetricKeys.RestingHeartRate,
        ChangePointLocal = changePoint,
        Direction = "up",
        PreLevelMedian = 52,
        PostLevelMedian = 57,
        OpenedLocal = changePoint.AddDays(14),
        ReleasedLocal = released,
        ReleaseReason = released is null ? null : "explained",
    };

    [Fact]
    public void AnOpenHoldKeepsItsFindingOpen_HoweverOldTheStep()
    {
        // Four months old: far outside the window in which a fresh regime change is
        // reported. The hold must still say so, or the frozen baseline is invisible.
        var hold = Held(Today.AddDays(-120));

        var findings = FindingRun.Detect(new FindingRunInputs([], [], [], Today, Holds: [hold]));

        var f = Assert.Single(findings, x => x.Type == FindingTypes.RegimeChange);
        Assert.Equal("high", f.Severity);
        Assert.Contains("held at 52", f.Summary);
        Assert.Contains("\"held\":true", f.EvidenceJson);
    }

    [Fact]
    public void AReleasedHoldRaisesNothing()
    {
        var hold = Held(Today.AddDays(-120), released: Today.AddDays(-3));
        var findings = FindingRun.Detect(new FindingRunInputs([], [], [], Today, Holds: [hold]));
        Assert.DoesNotContain(findings, x => x.Type == FindingTypes.RegimeChange);
    }

    [Fact]
    public async Task AcceptingAnOpenHoldIsRecorded()
    {
        var repo = new FakeRepo();
        var hold = Held(new DateOnly(2026, 6, 1));
        repo.HoldData.Add(hold);

        var result = await new RegimeAcceptancesController(repo)
            .Post(new(MetricKeys.RestingHeartRate, null, "2026-06-01", "new job, more stairs"));

        Assert.IsType<OkObjectResult>(result);
        var saved = Assert.Single(repo.AcceptanceData);
        Assert.Equal(hold.ChangePointLocal, saved.ChangePointLocal);
        Assert.Equal("new job, more stairs", saved.Note);
    }

    [Fact]
    public async Task AcceptingAStepThatIsNotHeldIsRefused()
    {
        var repo = new FakeRepo();
        repo.HoldData.Add(Held(new DateOnly(2026, 6, 1), released: new DateOnly(2026, 7, 1)));
        var ctrl = new RegimeAcceptancesController(repo);

        Assert.IsType<NotFoundObjectResult>(await ctrl.Post(new(MetricKeys.RestingHeartRate, null, "2026-05-01", null)));
        Assert.IsType<ConflictObjectResult>(await ctrl.Post(new(MetricKeys.RestingHeartRate, null, "2026-06-01", null)));
        Assert.Empty(repo.AcceptanceData);
    }
}
