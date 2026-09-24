using Microsoft.AspNetCore.Mvc;
using Vitara.Insight.Health;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Insight.Controllers;

// The read surface for the derived layer.
//
// Everything below was being computed and stored with nothing able to read it: no
// endpoint, so no dashboard, no MCP tool and no way for San to mention any of it. The
// statistics were sound and the system said nothing.
//
// Deliberately separate from the per-metric controllers next to it. Those answer "what
// was my readiness score" from the raw Oura tables; these answer "what is normal for
// me, and is anything off it", which is a different question over different rows.
[ApiController, Route("api/health")]
public class HealthIntelligenceController(IVitaraRepository repo) : ControllerBase
{
    // What Vitara currently believes is going on.
    [HttpGet("findings")]
    public async Task<IActionResult> Findings([FromQuery] bool includeResolved = false, [FromQuery] int limit = 100)
    {
        var findings = await repo.GetFindingsAsync(!includeResolved, limit);
        var today = LocalTime.Today;

        return Ok(findings.Select(f => new
        {
            f.Key,
            f.Type,
            f.Metric,
            f.Direction,
            f.Severity,
            f.Confidence,
            f.Summary,
            firstDetected = f.FirstDetectedLocal.ToString("yyyy-MM-dd"),
            lastDetected = f.LastDetectedLocal.ToString("yyyy-MM-dd"),
            resolved = f.ResolvedLocal?.ToString("yyyy-MM-dd"),
            f.IsActive,

            // How long this has been true. The whole reason FirstDetectedLocal is left
            // alone when a finding continues -- "for the fourth morning" is a different
            // statement from "this morning", and only one of them is worth acting on.
            daysRunning = f.LastDetectedLocal.DayNumber - f.FirstDetectedLocal.DayNumber + 1,

            // Whether it is still current. A finding last detected four days ago is
            // stale even if nothing has formally resolved it, which happens when the
            // worker stops running -- and a stale finding read as current is worse
            // than no finding at all.
            daysSinceDetected = today.DayNumber - f.LastDetectedLocal.DayNumber,

            evidence = f.EvidenceJson,
        }));
    }

    // What normal currently looks like, per metric.
    [HttpGet("baselines")]
    public async Task<IActionResult> Baselines()
    {
        var day = await repo.GetLatestBaselineDayAsync();
        if (day is null) return Ok(new { computedOn = (string?)null, baselines = Array.Empty<object>() });

        var baselines = await repo.GetBaselinesAsync(day.Value);

        return Ok(new
        {
            computedOn = day.Value.ToString("yyyy-MM-dd"),
            daysAgo = LocalTime.Today.DayNumber - day.Value.DayNumber,
            baselines = baselines
                .OrderBy(b => b.Metric)
                .Select(b => new
                {
                    b.Metric,
                    signature = b.BaselineSignature,
                    b.Mean,
                    b.StdDev,
                    b.Median,
                    b.P25,
                    b.P75,
                    b.N,

                    // Exposed rather than filtered out. "Not enough data yet" is a real
                    // answer and a useful one; silently omitting the metric looks
                    // identical to the metric not existing.
                    b.IsValid,
                    b.WindowDays,
                    regimeStart = b.RegimeStartLocal?.ToString("yyyy-MM-dd"),
                    exclusions = b.ExclusionsJson,
                }),
        });
    }

    // Today's z-scores, ratios and slopes, with what fed them.
    [HttpGet("derived")]
    public async Task<IActionResult> Derived([FromQuery] int days = 14)
    {
        var to = LocalTime.Today;
        var derived = await repo.GetDerivedMetricsAsync(to.AddDays(-Math.Clamp(days, 1, 365)), to);

        return Ok(derived.Select(d => new
        {
            d.Metric,
            day = d.ObservedDateLocal.ToString("yyyy-MM-dd"),
            d.Value,

            // Auditability is the point. A derived number nobody can trace back to its
            // inputs is indistinguishable from one the system made up.
            inputs = d.InputsJson,
        }));
    }

    // Standing relationships between what the user does and how they recover.
    [HttpGet("correlations")]
    public async Task<IActionResult> Correlations()
    {
        var found = await repo.GetLatestCorrelationsAsync();

        return Ok(new
        {
            computedOn = found.Count == 0 ? null : found[0].ComputedOnLocal.ToString("yyyy-MM-dd"),

            // Said in the payload, not just in the UI. Anything reading this -- the tab,
            // San, a future export -- has to carry the caveat with the number, and the
            // one place that guarantees it is next to the number.
            caveat = "These are correlations over the window, not causes. A relationship here " +
                     "means the two moved together, which can happen because one drives the " +
                     "other, because something else drives both, or by chance.",

            method = new
            {
                test = "Spearman rank correlation",
                minPairedDays = Vitara.Insight.Health.Correlations.MinPairedDays,
                minAbsRho = Vitara.Insight.Health.Correlations.MinAbsRho,
                multipleComparisons = "Benjamini-Hochberg, FDR 0.10, across every pair tested in the run",
            },

            correlations = found.Select(c => new
            {
                c.Driver,
                c.Outcome,
                c.LagDays,
                c.Rho,
                direction = c.Rho > 0 ? "positive" : "negative",
                c.N,
                pValue = Math.Round(c.PValue, 5),
                c.WindowDays,
            }),
        });
    }

    // Everything the system can know about you, one row per metric, whether or not it
    // has ever held a reading.
    //
    // The UI could previously only show metrics that happened to have data, so "we track
    // nothing of the sort" and "we track that and you have not recorded any" looked
    // identical -- and a zero looked the same as a gap. Every row here carries its own
    // state and what would fill it: no silent absences, and nothing to guess at.
    [HttpGet("metrics")]
    public async Task<IActionResult> Metrics()
    {
        var today = LocalTime.Today;

        // A year: enough to answer "when did you last see this" for a lab drawn twice a
        // year, without loading the whole history for a page of current values.
        var observations = await repo.GetObservationsAsync(today.AddDays(-400), today);
        var derived = await repo.GetDerivedMetricsAsync(today.AddDays(-400), today);
        var baselineDay = await repo.GetLatestBaselineDayAsync();
        var baselines = baselineDay is null ? [] : await repo.GetBaselinesAsync(baselineDay.Value);

        var rows = MetricCatalogue.All.Select(info =>
        {
            var latest = info.Computed
                ? derived.Where(d => d.Metric == info.Key)
                    .OrderBy(d => d.ObservedDateLocal)
                    .Select(d => (Day: (DateOnly?)d.ObservedDateLocal, Value: (double?)d.Value))
                    .LastOrDefault()
                : observations.Where(o => o.Metric == info.Key)
                    .OrderBy(o => o.ObservedDateLocal)
                    .Select(o => (Day: (DateOnly?)o.ObservedDateLocal, Value: (double?)o.Value))
                    .LastOrDefault();

            var readings = info.Computed
                ? derived.Count(d => d.Metric == info.Key)
                : observations.Count(o => o.Metric == info.Key);

            // The best-supported bucket when a metric is split by context -- blood
            // pressure has one per position, and the page shows the one with the most
            // behind it rather than an arbitrary first.
            var baseline = baselines.Where(b => b.Metric == info.Key).OrderByDescending(b => b.N).FirstOrDefault();

            var daysSince = latest.Day is { } day ? today.DayNumber - day.DayNumber : (int?)null;

            // Four states, each said out loud rather than inferred from a missing field.
            var state =
                latest.Value is null ? "no_data"
                : daysSince > info.StaleAfterDays ? "stale"
                : "current";

            var baselineState =
                baseline is null ? "none"
                : baseline.IsValid ? "ready"
                : "learning";

            return new
            {
                info.Key,
                info.Label,
                info.Unit,
                info.Group,
                info.Tier,
                info.Source,
                info.What,
                info.Decimals,
                info.Polarity,
                state,
                latest = latest.Value is null ? null : new
                {
                    value = Math.Round(latest.Value.Value, info.Decimals + 2),
                    day = latest.Day!.Value.ToString("yyyy-MM-dd"),
                    daysAgo = daysSince,
                },
                readings,
                staleAfterDays = info.StaleAfterDays,
                baseline = baseline is null ? null : new
                {
                    state = baselineState,
                    signature = baseline.BaselineSignature,
                    median = Math.Round(baseline.Median, info.Decimals + 2),
                    p25 = Math.Round(baseline.P25, info.Decimals + 2),
                    p75 = Math.Round(baseline.P75, info.Decimals + 2),
                    baseline.N,
                    needs = Math.Max(0, HealthThresholds.MinBaselineN - baseline.N),
                    baseline.WindowDays,
                    regimeStart = baseline.RegimeStartLocal?.ToString("yyyy-MM-dd"),
                    exclusions = baseline.ExclusionsJson,
                },
                // Said per row, because "we cannot fill this for you" is the answer to
                // most empty cells here and the user is the only one who can act on it.
                fillWith = info.Source switch
                {
                    "ring" => "Connect your ring and let it sync overnight",
                    "phone" => "Turn on the companion app's health sync",
                    "manual" => "Record it on the Record tab",
                    "lab" => "Add your latest blood test results",
                    _ => "Computed once the readings behind it exist",
                },
            };
        });

        return Ok(new
        {
            today = today.ToString("yyyy-MM-dd"),
            computedThrough = baselineDay?.ToString("yyyy-MM-dd"),
            minReadingsForBaseline = HealthThresholds.MinBaselineN,
            groups = MetricCatalogue.Groups,
            metrics = rows,
        });
    }

    // Who this person is, physiologically, and what tomorrow looks like.
    //
    // The bio signature: a portable description of how one body runs -- when it sleeps,
    // which days go badly, what it responds to, how long it takes to come back -- and,
    // separately, a forecast that has had to earn the right to be shown.
    //
    // Nothing here is compared with a population. The claim is never "you are above
    // average", it is "this is how you run, and here is how confidently we can say it",
    // and every part reports its own confidence or says it has nothing yet.
    [HttpGet("signature")]
    public async Task<IActionResult> BioSignature([FromQuery] int days = 400)
    {
        var to = LocalTime.Today;
        var from = to.AddDays(-Math.Clamp(days, 60, 1825));

        var rows = await DayRows(from, to);
        var nights = await repo.GetSleepAsync(from, to);
        var correlations = await repo.GetLatestCorrelationsAsync();
        var baselineDay = await repo.GetLatestBaselineDayAsync();
        var baselines = baselineDay is null ? [] : await repo.GetBaselinesAsync(baselineDay.Value);

        var chronotype = Signature.WhenTheySleep(nights);
        var week = Signature.TheirWeek(rows);
        var recovery = Signature.HowTheyRecover(rows);
        var confidence = Signature.HowSure(baselines, rows.Count);

        return Ok(new
        {
            version = 1,
            generatedOn = to.ToString("yyyy-MM-dd"),

            // First, so anything reading this knows how much weight it can bear.
            confidence = new
            {
                confidence.Settled,
                confidence.Learning,
                confidence.DaysOfHistory,
                confidence.Note,
            },

            sleepClock = chronotype is null
                ? null
                : new
                {
                    bedtime = Clock(chronotype.BedMinutes),
                    wake = Clock(chronotype.WakeMinutes),
                    bedtimeVariabilityMinutes = Math.Round(chronotype.BedVariabilityMinutes),
                    socialJetlagMinutes = Math.Round(chronotype.SocialJetlagMinutes),
                    chronotype.Nights,
                    chronotype.Note,
                },

            week = week is null
                ? null
                : new
                {
                    byDay = week.ByDay.ToDictionary(kv => kv.Key.ToString(), kv => Math.Round(kv.Value, 1)),
                    best = week.Best.ToString(),
                    worst = week.Worst.ToString(),
                    spread = Math.Round(week.Spread, 1),
                    week.Weeks,
                    week.Note,
                },

            recovery = new { days = recovery.Days, recovery.Episodes, recovery.Note },

            respondsTo = Signature.WhatMovesThem(correlations).Select(r => new
            {
                driver = MetricCatalogue.Find(r.Driver)?.Label ?? r.Driver,
                outcome = MetricCatalogue.Find(r.Outcome)?.Label ?? r.Outcome,
                r.LagDays,
                r.Rho,
                r.N,
            }),

            // The caveat travels with the numbers, as everywhere else.
            respondsToCaveat = "These moved together in your own data over the window. Moving together is " +
                               "not causing, and some of them will be coincidence.",

            // Everything above describes the past. This is the part that can be wrong.
            tomorrow = new
            {
                readiness = Shape(Prediction.Next(rows, Prediction.Target.Readiness)),
                restingHeartRate = Shape(Prediction.Next(rows, Prediction.Target.RestingHr)),
            },
        });
    }

    // Tomorrow, on its own, for anything that wants the forecast without the portrait.
    [HttpGet("forecast")]
    public async Task<IActionResult> Forecast([FromQuery] int days = 400)
    {
        var to = LocalTime.Today;
        var rows = await DayRows(to.AddDays(-Math.Clamp(days, 60, 1825)), to);

        return Ok(new
        {
            readiness = Shape(Prediction.Next(rows, Prediction.Target.Readiness)),
            restingHeartRate = Shape(Prediction.Next(rows, Prediction.Target.RestingHr)),
            hrv = Shape(Prediction.Next(rows, Prediction.Target.Hrv)),
            timeAsleep = Shape(Prediction.Next(rows, Prediction.Target.SleepMinutes)),
            minTrainingDays = Prediction.MinTrainingDays,
        });
    }

    // What if I had slept more?
    //
    // The step from a forecast to something worth calling a clone. It refuses in two
    // cases rather than guessing: when no model beat the dull answer there is no lever
    // to pull, and when the scenario is outside what this person has actually done
    // their data cannot answer it. Both refusals are the answer, not an error.
    [HttpGet("simulate")]
    public async Task<IActionResult> Simulate([FromQuery] string target = "readiness", [FromQuery] int days = 400)
    {
        var to = LocalTime.Today;
        var rows = await DayRows(to.AddDays(-Math.Clamp(days, 60, 1825)), to);

        var which = target.ToLowerInvariant() switch
        {
            "resting_hr" or "restinghr" => Prediction.Target.RestingHr,
            "hrv" => Prediction.Target.Hrv,
            "sleep" or "time_asleep" => Prediction.Target.SleepMinutes,
            _ => Prediction.Target.Readiness,
        };

        // A fixed set rather than free input. These are the two things a person can
        // actually decide tonight, at sizes they might plausibly decide on.
        (string, double)[] asks =
        [
            ("sleep", 60), ("sleep", 30), ("sleep", -60),
            ("effort", 250), ("effort", -250),
        ];

        var scenarios = Prediction.WhatIf(rows, which, asks);

        return Ok(new
        {
            target = Prediction.Describe(which),
            unit = Prediction.UnitOf(which),

            // First and unavoidably. Everything below is an association inside one
            // person's history, and the sentence that says so travels with the numbers
            // rather than sitting in a footnote nobody reads.
            caveat = "These come from what your own days did together, not from an experiment. " +
                     "An evening with more sleep in it usually differs in other ways too, and the " +
                     "model is carrying all of them.",

            scenarios = scenarios.Select(x => new
            {
                x.Lever,
                x.Delta,
                x.Question,
                x.From,
                x.To,
                x.Change,
                x.Supported,
                x.Answer,
            }),
        });
    }

    // The signature as a file: everything needed to read this person's shape, and to
    // run their forecast, without their raw readings.
    //
    // This is the portable half of the idea. What leaves is derived statistics and a
    // handful of fitted weights -- no sleep sessions, no heart-rate samples, no days.
    // Someone holding this file can say what tomorrow looks like and cannot say what
    // last Tuesday was.
    [HttpGet("signature/export")]
    public async Task<IActionResult> Export([FromQuery] int days = 400)
    {
        var to = LocalTime.Today;
        var from = to.AddDays(-Math.Clamp(days, 60, 1825));

        var rows = await DayRows(from, to);
        var nights = await repo.GetSleepAsync(from, to);
        var correlations = await repo.GetLatestCorrelationsAsync();
        var baselineDay = await repo.GetLatestBaselineDayAsync();
        var baselines = baselineDay is null ? [] : await repo.GetBaselinesAsync(baselineDay.Value);

        var chronotype = Signature.WhenTheySleep(nights);
        var week = Signature.TheirWeek(rows);
        var recovery = Signature.HowTheyRecover(rows);
        var confidence = Signature.HowSure(baselines, rows.Count);

        Prediction.Target[] targets =
        [
            Prediction.Target.Readiness, Prediction.Target.RestingHr,
            Prediction.Target.Hrv, Prediction.Target.SleepMinutes,
        ];

        var models = targets.Select(t =>
        {
            var evidence = Prediction.Run(rows, t);
            var model = evidence.ModelWins ? Prediction.Fit(rows, t, rows.Count - 1) : null;

            return new
            {
                target = Prediction.Describe(t),
                unit = Prediction.UnitOf(t),
                features = Prediction.FeatureNames(t),

                // Only exported when it earned its place. A model that lost to "tomorrow
                // is like today" would travel as a set of weights with no warning
                // attached, and be run by whoever received it.
                weights = model?.InOriginalUnits().Select(w => Math.Round(w, 6)),
                intercept = model is null ? (double?)null : Math.Round(model.Intercept, 4),
                centres = model?.Mean.Select(m => Math.Round(m, 4)),
                trainedOnDays = model?.TrainedOn,
                method = evidence.ModelWins ? "model" : "today",
                evidence.Verdict,
            };
        }).ToList();

        return Ok(new
        {
            schema = "maaya.vitara.signature",
            version = 2,
            generatedOn = to.ToString("yyyy-MM-dd"),

            contains = "Derived statistics and fitted weights only. No individual readings, no dates " +
                       "of any specific day, no raw sleep or heart-rate data.",
            disclaimer = "Built from consumer wearable data for one person. Not a medical record and " +
                         "not a diagnostic instrument.",

            confidence = new { confidence.Settled, confidence.Learning, confidence.DaysOfHistory, confidence.Note },

            // The levels, without the days that made them.
            normals = baselines.Select(b => new
            {
                metric = b.Metric,
                label = MetricCatalogue.Find(b.Metric)?.Label ?? b.Metric,
                signature = b.BaselineSignature,
                median = Math.Round(b.Median, 3),
                p25 = Math.Round(b.P25, 3),
                p75 = Math.Round(b.P75, 3),
                spread = Math.Round(b.StdDev, 3),
                b.N,
                b.IsValid,
                b.WindowDays,
            }),

            sleepClock = chronotype is null ? null : new
            {
                bedtime = Clock(chronotype.BedMinutes),
                wake = Clock(chronotype.WakeMinutes),
                bedtimeVariabilityMinutes = Math.Round(chronotype.BedVariabilityMinutes),
                socialJetlagMinutes = Math.Round(chronotype.SocialJetlagMinutes),
                chronotype.Nights,
            },

            week = week is null ? null : new
            {
                byDay = week.ByDay.ToDictionary(kv => kv.Key.ToString(), kv => Math.Round(kv.Value, 1)),
                best = week.Best.ToString(),
                worst = week.Worst.ToString(),
                spread = Math.Round(week.Spread, 1),
            },

            recovery = new { days = recovery.Days, recovery.Episodes },

            respondsTo = Signature.WhatMovesThem(correlations, 10).Select(r => new
            {
                driver = MetricCatalogue.Find(r.Driver)?.Label ?? r.Driver,
                outcome = MetricCatalogue.Find(r.Outcome)?.Label ?? r.Outcome,
                r.LagDays,
                r.Rho,
                r.N,
            }),

            forecasts = models,
        });
    }

    // The forecast is shown with its own record attached, always. A prediction whose
    // track record is available but not shown is a prediction being flattered.
    private static object? Shape(Prediction.Forecast? f) => f is null
        ? null
        : new
        {
            day = f.Day.ToString("yyyy-MM-dd"),
            value = Math.Round(f.Value, 1),
            low = Math.Round(f.Low, 1),
            high = Math.Round(f.High, 1),
            f.Method,
            f.Basis,
            evidence = new
            {
                modelMae = f.Evidence.Model is null ? (double?)null : Math.Round(f.Evidence.Model.Mae, 2),
                persistenceMae = Math.Round(f.Evidence.Persistence.Mae, 2),
                averageMae = Math.Round(f.Evidence.Mean.Mae, 2),
                skill = f.Evidence.Skill is null ? (double?)null : Math.Round(f.Evidence.Skill.Value, 3),
                testedOnDays = f.Evidence.Model?.Predictions ?? f.Evidence.Persistence.Predictions,
                f.Evidence.ModelWins,
                f.Evidence.Verdict,
            },
        };

    private static string Clock(double minutesFromMidnight)
    {
        var minutes = ((int)Math.Round(minutesFromMidnight) % 1440 + 1440) % 1440;
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    // One row per day, assembled from the tables that own each piece. Built here rather
    // than in the forecast so that the model sees exactly what the rest of the module
    // sees, with the same day boundaries.
    private async Task<List<Prediction.DayRow>> DayRows(DateOnly from, DateOnly to)
    {
        var readiness = await repo.GetReadinessAsync(from, to);
        var activity = await repo.GetActivityAsync(from, to);
        var sleep = await repo.GetSleepAsync(from, to);

        var byDay = readiness.ToDictionary(r => r.Day);
        var activityByDay = activity.GroupBy(a => a.Day).ToDictionary(g => g.Key, g => g.Last());
        var sleepByDay = sleep.GroupBy(x => x.Day).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.TotalSleepMinutes).First());

        var rows = new List<Prediction.DayRow>();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            byDay.TryGetValue(day, out var r);
            activityByDay.TryGetValue(day, out var a);
            sleepByDay.TryGetValue(day, out var night);

            // A day with nothing at all is still a row. Dropping it would close the gap
            // and let a fortnight without the ring look like a continuous fortnight.
            rows.Add(new Prediction.DayRow(
                day,
                Readiness: r?.Score,
                RestingHr: r?.RestingHeartRate,
                Hrv: night?.AvgHrv,
                SleepMinutes: night?.TotalSleepMinutes,
                ActiveCalories: a?.ActiveCalories));
        }

        return rows;
    }

    // Did the early-illness signal actually work?
    //
    // The one detector here with ground truth available: the user marks the days they
    // were ill so those days stay out of their baselines, and those marks are exactly
    // the labels an evaluation needs. Without this, every threshold in that detector
    // can be defended and none of it can be tested -- which is how a detector ends up
    // tuned to whatever produced a comfortable number of alerts in its first month.
    //
    // Deliberately unflattering. Two marked illnesses produce a sentence saying two is
    // not an evaluation, rather than a percentage that reads like one.
    [HttpGet("illness-eval")]
    public async Task<IActionResult> IllnessEvaluation([FromQuery] int days = 365)
    {
        var to = LocalTime.Today;
        var from = to.AddDays(-Math.Clamp(days, 30, 1825));

        var observations = await repo.GetObservationsAsync(from, to);
        var derived = await repo.GetDerivedMetricsAsync(from, to);
        var excluded = await repo.GetExcludedPeriodsAsync();

        var vitals = FindingRun.BuildVitals(derived, observations);
        var result = IllnessEval.Evaluate(
            vitals,
            excluded.Where(p => p.EndLocal >= from).ToList(),
            HealthThresholdSet.FromConfiguration(),
            HealthThresholds.IllnessSustainedDays);

        return Ok(new
        {
            windowDays = to.DayNumber - from.DayNumber,
            result.DaysEvaluated,

            // Counts and lists are named apart deliberately: serialised with a camelCase
            // policy, "Episodes" and "episodes" collide into one key and the payload
            // silently carries whichever was written last.
            episodeCount = result.Episodes,
            result.Caught,
            result.Missed,
            falseAlarmCount = result.FalseAlarms,

            // Positive is a warning, zero or negative is a confirmation. Reported
            // separately from the catch rate because they are worth different amounts
            // and averaging them together would hide which one this detector gives.
            medianLeadDays = result.MedianLeadDays,

            // The sentence first. A caller that prints only one field should print this.
            result.Verdict,

            thresholds = new
            {
                restingHrZ = HealthThresholds.IllnessRestingHrZ,
                hrvZ = HealthThresholds.IllnessHrvZ,
                tempZ = HealthThresholds.IllnessTempZ,
                tempFloorC = HealthThresholds.IllnessTempFloorC,
                tempOverrideC = HealthThresholds.IllnessTempOverrideC,
                sustainedDays = HealthThresholds.IllnessSustainedDays,
                leadWindowDays = IllnessEval.LeadWindowDays,
            },

            episodes = result.PerEpisode.Select(e => new
            {
                start = e.Start.ToString("yyyy-MM-dd"),
                end = e.End.ToString("yyyy-MM-dd"),
                e.Caught,
                e.LeadDays,
                signalStart = e.SignalStart?.ToString("yyyy-MM-dd"),
                e.Notes,
            }),

            falseAlarms = result.FalseAlarmRuns.Select(r => new
            {
                start = r.Start.ToString("yyyy-MM-dd"),
                end = r.End.ToString("yyyy-MM-dd"),
                days = r.End.DayNumber - r.Start.DayNumber + 1,
            }),
        });
    }

    // One call for "how am I doing", shaped for a model rather than a chart.
    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var today = LocalTime.Today;
        var findings = await repo.GetFindingsAsync(activeOnly: true, limit: 50);
        var baselineDay = await repo.GetLatestBaselineDayAsync();
        var latestData = await repo.GetLatestObservationDayAsync();

        var daysBehind = baselineDay is null ? (int?)null : today.DayNumber - baselineDay.Value.DayNumber;

        // Which few lead today. Everything active is still active and still listed by
        // /findings -- this only decides what gets said first, because a list that is
        // long and mostly unchanged every morning is a list that stops being read.
        var chosen = Surfacing.Choose(findings);

        // How much of the picture has a normal yet.
        //
        // Without this, a metric with four readings behind it and a metric with ninety
        // read identically once they are inside their range, and "nothing is flagged"
        // covers both. A model told only about findings will say the reassuring thing.
        var baselines = baselineDay is null ? [] : await repo.GetBaselinesAsync(baselineDay.Value);
        var learning = baselines.Where(b => !b.IsValid).Select(b => b.Metric).Distinct().ToList();
        var settled = baselines.Where(b => b.IsValid).Select(b => b.Metric).Distinct().Count();

        // Said in words, because the dates alone were not enough.
        //
        // Asked "is anything wrong with my health", San answered "nothing is flagged as
        // being outside your normal range" -- from an empty findings list, on a box
        // where the analysis had not run once. Zero findings and no analysis are the
        // same JSON shape, and the model read the reassuring one. computedThrough and
        // daysBehind were both sitting right there and it went straight past them.
        //
        // A number a model may or may not interpret is not a safeguard. A sentence that
        // says "this has never been computed, do not tell the user they are fine" is.
        var status =
            baselineDay is null
                ? "NO ANALYSIS YET. Baselines have never been computed, so an empty findings list means " +
                  "nothing has been checked - it does NOT mean the user is fine. Say that plainly."
            : daysBehind >= 3
                ? $"STALE. Last computed {daysBehind} days ago, so findings may be out of date. " +
                  "Say so before reporting them."
            : findings.Count == 0
                ? $"Analysis current through {baselineDay:yyyy-MM-dd}. Nothing is outside this user's " +
                  "normal range." + LearningClause(learning.Count)
                : $"Analysis current through {baselineDay:yyyy-MM-dd}." +
                  (chosen.Standing.Count > 0
                      ? $" The {chosen.Surfaced.Count} findings below are what leads today; " +
                        $"{chosen.Standing.Count} more are standing and unchanged."
                      : "");

        return Ok(new
        {
            // Recency first and unavoidably. San reads this, and an analysis with no
            // date attached reads as today's no matter how old it is.
            status,
            latestDataDay = latestData?.ToString("yyyy-MM-dd"),
            computedThrough = baselineDay?.ToString("yyyy-MM-dd"),
            daysBehind,

            activeFindings = findings.Count,
            bySeverity = findings.GroupBy(f => f.Severity).ToDictionary(g => g.Key, g => g.Count()),

            // How many metrics have earned a normal, and how many have not. A caller
            // reporting "nothing is flagged" over eleven settled metrics and thirty
            // unsettled ones is reporting the wrong thing.
            coverage = new
            {
                metricsWithNormal = settled,
                stillLearning = learning.Count,
                stillLearningMetrics = learning
                    .Select(m => MetricCatalogue.Find(m)?.Label ?? m)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .Take(8),
                note = learning.Count == 0
                    ? ""
                    : $"{learning.Count} metrics do not have enough readings for a normal yet. For those, " +
                      "say 'not enough data yet' rather than implying they are fine.",
            },

            // The cap, said out loud. A caller that shortens the list on its own has no
            // way to tell the reader that it did.
            surfacing = new { cap = chosen.Cap, held = chosen.Standing.Count, note = chosen.Note },

            findings = chosen.Surfaced.Select(f => new
            {
                f.Key,
                f.Type,
                // Carried even though Summary already names it in prose. The key is
                // colon-delimited and the summary is a sentence; a caller that wants
                // to group or label by metric should not have to parse either.
                f.Metric,
                f.Severity,
                f.Summary,
                daysRunning = Surfacing.DaysRunning(f),
                surfaced = true,
            }),

            // Present, briefly. Held back from the lead is not the same as hidden, and a
            // caller asked "is that everything?" must be able to answer truthfully.
            standing = chosen.Standing.Select(f => new
            {
                f.Key,
                f.Type,
                f.Metric,
                f.Severity,
                f.Summary,
                daysRunning = Surfacing.DaysRunning(f),
                surfaced = false,
            }),
        });
    }

    private static int Rank(string severity) => severity switch
    {
        "high" => 3,
        "notable" => 2,
        _ => 1,
    };

    // Said on the reassuring branch specifically. "Nothing is outside your normal range"
    // is true and misleading when half the metrics have no normal to be outside of.
    private static string LearningClause(int learning) => learning switch
    {
        0 => "",
        1 => " One metric is still learning its normal and was not checked.",
        _ => $" {learning} metrics are still learning their normal and were not checked.",
    };
}
