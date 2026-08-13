using Vitara.Application;
using Vitara.Domain.Entities;

namespace Vitara.Tests;

// This makes claims about the user's own body from a fortnight of data, which is
// exactly where a confident wrong number does damage. Most of these tests are about
// what it must REFUSE to say.
public class WorkoutImpactTests
{
    private static readonly DateOnly Day1 = new(2026, 7, 1);

    private static Workout Workout(int dayOffset, string activity = "running", string? intensity = "moderate")
        => new()
        {
            Id = $"w{dayOffset}-{activity}",
            Day = Day1.AddDays(dayOffset),
            Activity = activity,
            Intensity = intensity,
        };

    private static DailyReadiness Readiness(int dayOffset, int score)
        => new() { Id = $"r{dayOffset}", Day = Day1.AddDays(dayOffset), Score = score };

    // Rest mornings score 80; the morning after a hard session scores 65.
    private static (List<Workout>, List<DailyReadiness>) HardSessionsCostRecovery()
    {
        var workouts = new List<Workout>();
        var readiness = new List<DailyReadiness>();
        for (var d = 0; d < 20; d++)
        {
            // Train on days 0, 4, 8, 12 — leaving plenty of untrained days for a baseline.
            var trained = d % 4 == 0;
            if (trained) workouts.Add(Workout(d, "weight_training", "hard"));
            var previousWasTraining = (d - 1) % 4 == 0 && d > 0;
            readiness.Add(Readiness(d, previousWasTraining ? 65 : 80));
        }
        return (workouts, readiness);
    }

    [Fact]
    public void FindsARealRecoveryCost()
    {
        var (w, r) = HardSessionsCostRecovery();
        var report = WorkoutImpact.Analyze(w, r);

        Assert.Equal(80, report.Baseline);
        var hard = report.Findings.Single(f => f.Dimension == "intensity" && f.Group == "hard");
        Assert.Equal(65, hard.AvgAfter);
        Assert.Equal(-15, hard.Delta);
        Assert.True(hard.Samples >= WorkoutImpact.MinSamples);
    }

    // The same day is reported under both dimensions — the activity and the intensity
    // are two views of one fact, not two independent findings.
    [Fact]
    public void ReportsBothActivityAndIntensity()
    {
        var (w, r) = HardSessionsCostRecovery();
        var report = WorkoutImpact.Analyze(w, r);
        Assert.Contains(report.Findings, f => f.Dimension == "activity" && f.Group == "weight_training");
        Assert.Contains(report.Findings, f => f.Dimension == "intensity" && f.Group == "hard");
    }

    // Guard 1: one or two mornings is an anecdote.
    [Fact]
    public void RefusesToReportFromTooFewSessions()
    {
        var workouts = new List<Workout> { Workout(2, "boxing", "hard"), Workout(6, "boxing", "hard") };
        var readiness = Enumerable.Range(0, 14).Select(d => Readiness(d, d is 3 or 7 ? 50 : 80)).ToList();

        var report = WorkoutImpact.Analyze(workouts, readiness);
        Assert.DoesNotContain(report.Findings, f => f.Group == "boxing");
    }

    // Guard 2: readiness drifts a couple of points on its own. Reporting that as a
    // training effect is noise with a decimal point.
    [Fact]
    public void IgnoresEffectsTooSmallToMeanAnything()
    {
        var workouts = Enumerable.Range(0, 5).Select(i => Workout(i * 3, "yoga", "easy")).ToList();
        var readiness = Enumerable.Range(0, 20)
            .Select(d => Readiness(d, (d - 1) % 3 == 0 && d > 0 ? 79 : 80))   // 1 point difference
            .ToList();

        var report = WorkoutImpact.Analyze(workouts, readiness);
        Assert.DoesNotContain(report.Findings, f => f.Group == "yoga");
    }

    // Guard 3: with no rest days there is nothing to compare against, and an average
    // that includes the training days would be measuring them against themselves.
    [Fact]
    public void SaysSoWhenThereIsNoBaseline()
    {
        var workouts = Enumerable.Range(0, 14).Select(d => Workout(d, "running")).ToList();
        var readiness = Enumerable.Range(0, 14).Select(d => Readiness(d, 70)).ToList();

        var report = WorkoutImpact.Analyze(workouts, readiness);
        Assert.Empty(report.Findings);
        Assert.Contains("rest days", report.Note);
    }

    [Fact]
    public void SaysSoWhenNothingStandsOut()
    {
        var workouts = Enumerable.Range(0, 5).Select(i => Workout(i * 3, "walking", "easy")).ToList();
        var readiness = Enumerable.Range(0, 20).Select(d => Readiness(d, 80)).ToList();

        var report = WorkoutImpact.Analyze(workouts, readiness);
        Assert.Empty(report.Findings);
        Assert.NotNull(report.Note);
    }

    // A training day with no readiness recorded for the next morning contributes
    // nothing — it must not be silently counted as a zero.
    [Fact]
    public void MissingNextMorningIsSkippedNotZeroed()
    {
        var (w, r) = HardSessionsCostRecovery();
        var withGap = r.Where(x => x.Day != Day1.AddDays(5)).ToList();   // drop one morning-after

        var report = WorkoutImpact.Analyze(w, withGap);
        var hard = report.Findings.Single(f => f.Dimension == "intensity" && f.Group == "hard");
        Assert.Equal(65, hard.AvgAfter);   // unchanged; the gap was skipped, not averaged in
    }

    // The biggest effect is the most likely thing worth changing, so it leads.
    [Fact]
    public void LargestEffectIsReportedFirst()
    {
        var workouts = new List<Workout>();
        var readiness = new List<DailyReadiness>();
        for (var d = 0; d < 30; d++)
        {
            if (d % 6 == 0) workouts.Add(Workout(d, "sprints", "hard"));
            else if (d % 6 == 3) workouts.Add(Workout(d, "walking", "easy"));

            var afterHard = d > 0 && (d - 1) % 6 == 0;
            var afterEasy = d > 0 && (d - 1) % 6 == 3;
            readiness.Add(Readiness(d, afterHard ? 55 : afterEasy ? 74 : 80));
        }

        var report = WorkoutImpact.Analyze(workouts, readiness);
        Assert.Equal("sprints", report.Findings[0].Group);
        Assert.True(Math.Abs(report.Findings[0].Delta) > Math.Abs(report.Findings[^1].Delta));
    }

    [Fact]
    public void EmptyInputIsHandled()
    {
        var report = WorkoutImpact.Analyze([], []);
        Assert.Empty(report.Findings);
        Assert.NotNull(report.Note);
    }
}
