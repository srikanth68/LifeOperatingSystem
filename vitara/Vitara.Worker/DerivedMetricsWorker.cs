using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vitara.Application.Health;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Worker;

// Turns what Oura reported into what it means: baselines, z-scores, load ratios,
// sleep debt, drift. Pure arithmetic, no model involvement anywhere.
//
// NOT a midnight job, despite the spec calling for one. The user syncs Oura in the
// morning, so at midnight the night that just finished has not arrived yet -- a job
// running then computes on absence, writes a baseline missing its most recent day, and
// reports staleness for data that is merely not downloaded yet.
//
// So it is driven by the data rather than by the clock. It wakes hourly, compares the
// newest observation against the newest baseline, and does nothing at all unless there
// is something new to compute. That makes it correct whenever the sync happens, and
// self-healing when the sync is late or fails: the work happens on the next wake after
// the data lands, without anyone scheduling around it.
public class DerivedMetricsWorker(IServiceProvider services, ILogger<DerivedMetricsWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(
        double.TryParse(Environment.GetEnvironmentVariable("VITARA_DERIVED_CHECK_HOURS"), out var h) && h > 0 ? h : 1);

    // Behind the Oura sync so the common case is that data is already waiting.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(6);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Derived metrics worker started. Checking every {h}h for new data.", CheckInterval.TotalHours);

        try { await Task.Delay(StartupDelay, ct); } catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await RunIfNewDataAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Derived metrics pass failed."); }

            try { await Task.Delay(CheckInterval, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunIfNewDataAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IVitaraRepository>();

        // Project first: observations are derived from the typed tables, so anything the
        // Oura sync wrote this morning has to become observations before it can be
        // baselined. Idempotent, so re-running costs a query and writes nothing.
        var projected = await ProjectAsync(repo);

        var latestData = await repo.GetLatestObservationDayAsync();
        if (latestData is null)
        {
            logger.LogInformation("Derived metrics: no observations yet.");
            return;
        }

        var lastComputed = await repo.GetLatestBaselineDayAsync();
        if (lastComputed is not null && lastComputed >= latestData)
        {
            // The usual outcome, twenty-three hours out of twenty-four. Logged at debug
            // so an hourly no-op does not fill the container log.
            logger.LogDebug("Derived metrics: nothing new (computed through {Day}).", lastComputed);
            return;
        }

        logger.LogInformation("Derived metrics: computing through {Day} ({Projected} new observations).",
            latestData, projected);

        await ComputeAsync(repo, latestData.Value, ct);
    }

    // Typed rows to flat observations. The typed tables remain the source of truth;
    // this is the second, flat view the analytics layer reads.
    private static async Task<int> ProjectAsync(IVitaraRepository repo)
    {
        // A wide window rather than just yesterday: Oura revises recent days, and a
        // backfill can land months at once. Re-projecting is free because the upsert
        // ignores what already exists.
        var to = LocalTime.Today;
        var from = to.AddDays(-120);

        var observations = new List<Observation>();

        foreach (var s in await repo.GetSleepAsync(from, to)) observations.AddRange(ObservationProjector.FromSleep(s));
        foreach (var r in await repo.GetReadinessAsync(from, to)) observations.AddRange(ObservationProjector.FromReadiness(r));
        foreach (var a in await repo.GetActivityAsync(from, to)) observations.AddRange(ObservationProjector.FromActivity(a));
        foreach (var s in await repo.GetStressAsync(from, to)) observations.AddRange(ObservationProjector.FromStress(s));
        foreach (var s in await repo.GetSpo2Async(from, to)) observations.AddRange(ObservationProjector.FromSpo2(s));
        foreach (var v in await repo.GetVo2MaxAsync(from, to)) observations.AddRange(ObservationProjector.FromVo2Max(v));
        foreach (var c in await repo.GetCardiovascularAgeAsync(from, to)) observations.AddRange(ObservationProjector.FromCardiovascularAge(c));
        foreach (var w in await repo.GetWeighInsAsync(from, to)) observations.AddRange(ObservationProjector.FromWeighIn(w));

        return await repo.UpsertObservationsAsync(observations);
    }

    private async Task ComputeAsync(IVitaraRepository repo, DateOnly asOf, CancellationToken ct)
    {
        var windowDays = HealthThresholds.BaselineWindowDays;

        // Reach back beyond the baseline window so regime detection has history to
        // compare against, and so drift has ninety days to fit a slope over.
        var observations = await repo.GetObservationsAsync(asOf.AddDays(-(windowDays + 120)), asOf);
        if (observations.Count == 0) return;

        var inputs = new BaselineInputs(
            observations,
            await repo.GetExcludedPeriodsAsync(),
            await repo.GetTravelPeriodsAsync(),
            await repo.GetDevicesAsync());

        var baselines = new List<Baseline>();
        var derived = new List<DerivedMetric>();

        // One baseline per metric per context bucket. Most metrics have exactly one
        // bucket; blood pressure and glucose are the exceptions, and that is the whole
        // reason signatures exist.
        foreach (var group in observations.GroupBy(o => (o.Metric, o.BaselineSignature)))
        {
            var (metric, signature) = group.Key;

            var baseline = BaselineCalculator.Compute(
                metric, signature, asOf, inputs,
                windowDays, HealthThresholds.MinBaselineN,
                HealthThresholds.RegimeShiftZ, HealthThresholds.RegimeDwellDays);

            baselines.Add(baseline);

            // A z-score against a baseline that has not earned validity would be a
            // confident number built on four readings. Better to have no z-score than
            // a meaningless one, because the meaningless one gets acted on.
            if (!baseline.IsValid) continue;

            var today = group.Where(o => o.ObservedDateLocal == asOf).ToList();
            if (today.Count == 0) continue;

            var z = Statistics.ZScore(today.Average(o => o.Value), baseline.Mean, baseline.StdDev);
            if (z is { } score)
                derived.Add(new DerivedMetric
                {
                    Metric = $"{metric}_z",
                    ObservedDateLocal = asOf,
                    Value = score,
                    InputsJson = $$"""{"mean":{{baseline.Mean:F3}},"sd":{{baseline.StdDev:F3}},"n":{{baseline.N}}}""",
                });
        }

        derived.AddRange(ComputeLoadAndSleep(observations, asOf));
        derived.AddRange(ComputeDrift(observations, asOf));

        await repo.SaveBaselinesAsync(baselines);
        await repo.SaveDerivedMetricsAsync(derived);

        logger.LogInformation("Derived metrics: {Baselines} baselines ({Valid} valid), {Derived} derived values.",
            baselines.Count, baselines.Count(b => b.IsValid), derived.Count);

        await DetectAsync(repo, observations, baselines, asOf);
    }

    // The step that makes the statistics speak.
    //
    // Everything above computes what is true; this decides what is worth saying. It is
    // kept separate and reads back from storage rather than reusing the in-memory
    // derived list, because a detector needs the last several days of z-scores and
    // this pass only computed today's.
    private async Task DetectAsync(
        IVitaraRepository repo, IReadOnlyList<Observation> observations,
        IReadOnlyList<Baseline> baselines, DateOnly asOf)
    {
        var derived = await repo.GetDerivedMetricsAsync(asOf.AddDays(-120), asOf);

        var findings = FindingRun.Detect(new FindingRunInputs(observations, baselines, derived, asOf));
        var sync = await repo.SyncFindingsAsync(findings, asOf);

        // Opened and resolved are events; continued is a condition that is still true
        // and must not be announced again. Logged separately so the container log shows
        // which of the three happened without anyone diffing row counts.
        if (sync.Opened > 0 || sync.Resolved > 0)
            logger.LogInformation(
                "Findings: {Opened} opened, {Continued} continuing, {Resolved} resolved — {Keys}",
                sync.Opened, sync.Continued, sync.Resolved,
                findings.Count == 0 ? "none" : string.Join(", ", findings.Select(f => f.Key)));
        else
            logger.LogDebug("Findings: {Continued} continuing, nothing new.", sync.Continued);
    }

    private static List<DerivedMetric> ComputeLoadAndSleep(IReadOnlyList<Observation> observations, DateOnly asOf)
    {
        var derived = new List<DerivedMetric>();

        // Strain accumulating faster than the body adapts to it, before it is felt.
        var calories = observations.Where(o => o.Metric == MetricKeys.ActiveCalories).ToList();
        var acute = calories.Where(o => o.ObservedDateLocal > asOf.AddDays(-7)).Select(o => o.Value).ToList();
        var chronic = calories.Where(o => o.ObservedDateLocal > asOf.AddDays(-28)).Select(o => o.Value).ToList();

        if (Statistics.AcuteChronicRatio(acute, chronic) is { } acwr)
            derived.Add(new DerivedMetric
            {
                Metric = "acwr_active_calories",
                ObservedDateLocal = asOf,
                Value = acwr,
                InputsJson = $$"""{"acuteDays":{{acute.Count}},"chronicDays":{{chronic.Count}}}""",
            });

        var nights = observations
            .Where(o => o.Metric == MetricKeys.TotalSleepMinutes)
            .Select(o => (o.ObservedDateLocal, o.Value))
            .ToList();

        if (nights.Count >= 7)
        {
            var need = SleepDebt.EstimateNeed(nights.Select(n => n.Value).ToList());
            var recent = nights.Where(n => n.ObservedDateLocal > asOf.AddDays(-14)).ToList();

            derived.Add(new DerivedMetric
            {
                Metric = "sleep_debt_minutes",
                ObservedDateLocal = asOf,
                Value = SleepDebt.Accumulate(recent, need.Minutes),
                // The basis travels with the number, because "you are four hours short"
                // means something different when the need it is measured against was
                // inferred rather than stated.
                InputsJson = $$"""{"needMinutes":{{need.Minutes:F0}},"basis":"{{need.Basis}}","nights":{{recent.Count}}}""",
            });
        }

        return derived;
    }

    // Slow movement, invisible day to day, and the highest-value signal here precisely
    // because nobody notices it happening.
    private static List<DerivedMetric> ComputeDrift(IReadOnlyList<Observation> observations, DateOnly asOf)
    {
        string[] slowMetrics = [MetricKeys.RestingHeartRate, MetricKeys.HrvRmssd, MetricKeys.WeightKg, MetricKeys.SystolicBp];
        var derived = new List<DerivedMetric>();

        foreach (var metric in slowMetrics)
            foreach (var window in new[] { 14, 30, 90 })
            {
                var points = observations
                    .Where(o => o.Metric == metric && o.ObservedDateLocal > asOf.AddDays(-window))
                    .Select(o => (o.ObservedDateLocal.DayNumber, o.Value))
                    .ToList();

                if (Statistics.SlopePerDay(points) is { } slope)
                    derived.Add(new DerivedMetric
                    {
                        Metric = $"{metric}_slope_{window}d",
                        ObservedDateLocal = asOf,
                        Value = slope,
                        InputsJson = $$"""{"n":{{points.Count}},"windowDays":{{window}}}""",
                    });
            }

        return derived;
    }
}
