using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// A held baseline (Vitara ADR 0017). Refusing to adopt an unexplained step the wrong way
// used to leave its readings in the window, so the worse level became normal within one
// window length anyway. These pin the fix: the baseline stays at the old level until
// something explains the step, the person accepts it, or it comes back on its own.
public class RegimeHoldTests
{
    private const string Metric = MetricKeys.RestingHeartRate;
    private static readonly DateOnly ChangePoint = new(2026, 6, 1);

    // Resting HR 52 for 120 days, then 57 from the change point onwards, SD about 1.2 --
    // the brief's own example. Seeded, so every run sees the same noise.
    internal static List<Observation> Step(DateOnly through, double before = 52, double after = 57,
        DateOnly? revertOn = null, string metric = Metric)
    {
        var rng = new Random(17);
        var list = new List<Observation>();
        for (var d = ChangePoint.AddDays(-120); d <= through; d = d.AddDays(1))
        {
            var level = d < ChangePoint ? before : revertOn is { } r && d >= r ? before : after;
            // Box-Muller: normal noise without a statistics package.
            var noise = Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
            list.Add(new Observation
            {
                Metric = metric,
                Value = Math.Round(level + 1.2 * noise, 1),
                ObservedDateLocal = d,
                ObservedAtLocal = d.ToDateTime(new TimeOnly(7, 0)),
                EligibleForBaseline = true,
            });
        }
        return list;
    }

    private static BaselineResult Run(DateOnly asOf, List<Observation> obs,
        List<RegimeHold>? holds = null, List<RegimeAcceptance>? acceptances = null,
        List<Intervention>? interventions = null) =>
        BaselineCalculator.ComputeWithHold(Metric, "", asOf,
            new BaselineInputs(obs, [], [], [], interventions ?? [], holds ?? [], acceptances ?? []),
            windowDays: 60, minN: 21, regimeShiftZ: 1.5, regimeDwellDays: 14);

    // What the worker does between runs: persist an opened hold, apply a release.
    private static List<RegimeHold> Persist(List<RegimeHold> holds, HoldChange? change)
    {
        if (change is null) return holds;
        var others = holds.Where(h => h.ChangePointLocal != change.Hold.ChangePointLocal).ToList();
        others.Add(change.Hold);
        return others;
    }

    // Day by day, as the hourly worker would, carrying holds forward.
    private static (BaselineResult Last, List<RegimeHold> Holds) Replay(
        DateOnly from, DateOnly through, List<Observation> obs,
        List<RegimeAcceptance>? acceptances = null, List<Intervention>? interventions = null)
    {
        var holds = new List<RegimeHold>();
        BaselineResult? last = null;
        for (var d = from; d <= through; d = d.AddDays(1))
        {
            var day = d;
            last = Run(day, obs.Where(o => o.ObservedDateLocal <= day).ToList(), holds, acceptances, interventions);
            holds = Persist(holds, last.Hold);
        }
        return (last!, holds);
    }

    [Fact]
    public void AnUnexplainedRiseIsHeldAtTheOldLevel_ThroughDetectionAndLongAfter()
    {
        var obs = Step(ChangePoint.AddDays(70));

        var (at20, holdsAt20) = Replay(ChangePoint.AddDays(14), ChangePoint.AddDays(20), obs);
        Assert.InRange(at20.Baseline.Mean, 51.5, 52.5);
        Assert.Single(holdsAt20, h => h.IsOpen);

        // Seventy days on, the whole 60-day window is after the step. Without a stored
        // hold the detector would see no step at all and 57 would be "normal".
        var (at70, holdsAt70) = Replay(ChangePoint.AddDays(14), ChangePoint.AddDays(70), obs);
        Assert.InRange(at70.Baseline.Mean, 51.5, 52.5);
        Assert.Null(at70.Baseline.RegimeStartLocal);
        Assert.Contains("held_regime_shift", at70.Baseline.ExclusionsJson);

        var hold = Assert.Single(holdsAt70);
        Assert.True(hold.IsOpen);
        Assert.Equal("up", hold.Direction);
        Assert.InRange(hold.PreLevelMedian, 51, 53);
        Assert.InRange(hold.PostLevelMedian, 56, 58);
        Assert.True(hold.PreLevelSpread > 0);
    }

    [Fact]
    public void AMedicationEnteredLaterExplainsTheStep_AndTheNewLevelIsAdopted()
    {
        var obs = Step(ChangePoint.AddDays(40));
        var (_, holds) = Replay(ChangePoint.AddDays(14), ChangePoint.AddDays(30), obs);
        var open = Assert.Single(holds, h => h.IsOpen);

        // Added after the fact, starting three days before the level moved.
        var started = new List<Intervention>
        {
            new() { Kind = "medication", Name = "new prescription", StartedOnLocal = open.ChangePointLocal.AddDays(-3) },
        };
        var asOf = ChangePoint.AddDays(31);
        var next = Run(asOf, obs.Where(o => o.ObservedDateLocal <= asOf).ToList(), holds, interventions: started);

        Assert.Equal(HoldChange.Released, next.Hold?.Kind);
        Assert.Equal("explained", next.Hold!.Hold.ReleaseReason);
        Assert.Contains("new prescription", next.Hold.Hold.Explanation);
        Assert.Equal(open.ChangePointLocal, next.Baseline.RegimeStartLocal);
        Assert.InRange(next.Baseline.Mean, 56.5, 57.5);
    }

    [Fact]
    public void ThePersonAcceptingTheLevelReleasesTheHold()
    {
        var obs = Step(ChangePoint.AddDays(40));
        var (_, holds) = Replay(ChangePoint.AddDays(14), ChangePoint.AddDays(30), obs);
        var acceptances = new List<RegimeAcceptance>
        {
            new() { Metric = Metric, ChangePointLocal = holds[0].ChangePointLocal, AcceptedOnLocal = ChangePoint.AddDays(31), Note = "new job" },
        };

        var asOf = ChangePoint.AddDays(31);
        var next = Run(asOf, obs.Where(o => o.ObservedDateLocal <= asOf).ToList(), holds, acceptances);

        Assert.Equal("accepted", next.Hold?.Hold.ReleaseReason);
        Assert.NotNull(next.Baseline.RegimeStartLocal);
        Assert.InRange(next.Baseline.Mean, 56.5, 57.5);
    }

    [Fact]
    public void AStepThatComesBackOnItsOwnIsReleasedAsReverted()
    {
        // Three and a half weeks up, nobody marked an illness, then back to 52. The
        // brief's version had no exit for this and would have held it forever.
        var obs = Step(ChangePoint.AddDays(60), revertOn: ChangePoint.AddDays(24));
        var (last, holds) = Replay(ChangePoint.AddDays(14), ChangePoint.AddDays(60), obs);

        var hold = Assert.Single(holds);
        Assert.False(hold.IsOpen);
        Assert.Equal("reverted", hold.ReleaseReason);
        // The way back down is itself a step in the harmless direction, so it may be
        // adopted as a regime of its own. Either way the level is the old one.
        Assert.InRange(last.Baseline.Median, 51, 53);
    }

    [Fact]
    public void AStepTheHarmlessWayIsAdoptedAtOnce_WithNoHold()
    {
        var obs = Step(ChangePoint.AddDays(30), before: 52, after: 47);
        var (last, holds) = Replay(ChangePoint.AddDays(14), ChangePoint.AddDays(30), obs);

        Assert.Empty(holds);
        Assert.NotNull(last.Baseline.RegimeStartLocal);
        Assert.InRange(last.Baseline.Mean, 46.5, 47.5);
    }

    [Fact]
    public void TheSameStepIsNotHeldTwice()
    {
        var obs = Step(ChangePoint.AddDays(40));
        var (_, holds) = Replay(ChangePoint.AddDays(14), ChangePoint.AddDays(40), obs);
        Assert.Single(holds);
    }
}
