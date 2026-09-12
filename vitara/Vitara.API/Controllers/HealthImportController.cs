using Microsoft.AspNetCore.Mvc;
using Vitara.Application;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.API.Controllers;

// Uploading an Apple Health export as a spreadsheet.
//
// In Vitara rather than Insight, deliberately. This is ingestion, and ingestion and
// analysis were just split apart precisely so each has one home: data arrives here and
// lands in the typed tables, exactly as Oura's does, and Insight picks it up from
// there. An importer that wrote derived rows directly would bypass the projector and
// put a second path into the analysis.
//
// TWO CALLS, NOT ONE. A preview that writes nothing, then a commit. Apple Health
// exporters disagree about column names, date formats and whether a row is a day or a
// sample, so the importer has to guess -- and a guess about health data belongs in
// front of the user before it reaches the database, not after. The preview says what
// it recognised, what it ignored, and what it could not read.
[ApiController, Route("api/healthimport")]
public class HealthImportController(IVitaraRepository repo, ILogger<HealthImportController> logger) : ControllerBase
{
    // 25MB. A two-year daily export is well under a megabyte; anything approaching
    // this is raw samples, which belong in the Oura sync rather than an upload box.
    private const long MaxBytes = 25L * 1024 * 1024;

    [HttpPost("preview")]
    [RequestSizeLimit(MaxBytes)]
    public Task<IActionResult> Preview(IFormFile? file) => Handle(file, commit: false);

    [HttpPost("commit")]
    [RequestSizeLimit(MaxBytes)]
    public Task<IActionResult> Commit(IFormFile? file) => Handle(file, commit: true);

    private async Task<IActionResult> Handle(IFormFile? file, bool commit)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Attach a .csv, .tsv or .xlsx file." });

        if (file.Length > MaxBytes)
            return BadRequest(new { error = $"File is {file.Length / 1024 / 1024}MB; the limit is 25MB." });

        ImportPreview preview;
        try
        {
            await using var stream = file.OpenReadStream();
            preview = HealthImport.Parse(stream, file.FileName);
        }
        catch (Exception ex)
        {
            // A corrupt workbook or a file that is not a spreadsheet at all must come
            // back as a message, not a 500 with a stack trace in the browser.
            logger.LogWarning(ex, "Health import could not read {File}.", file.FileName);
            return BadRequest(new { error = $"Could not read that file: {ex.Message}" });
        }

        if (!commit || preview.Readings.Count == 0)
            return Ok(Describe(preview, written: null));

        var written = await WriteAsync(preview.Readings);

        logger.LogInformation(
            "Health import: {File} ({Shape}), {Readings} readings over {Days} day(s) — {Written}.",
            file.FileName, preview.Shape, preview.Readings.Count,
            preview.Readings.Select(r => r.Day).Distinct().Count(),
            string.Join(", ", written.Select(w => $"{w.Key} {w.Value}")));

        return Ok(Describe(preview, written));
    }

    private static object Describe(ImportPreview p, Dictionary<string, int>? written) => new
    {
        p.Shape,
        p.RowsRead,
        firstDay = p.FirstDay?.ToString("yyyy-MM-dd"),
        lastDay = p.LastDay?.ToString("yyyy-MM-dd"),
        days = p.Readings.Select(r => r.Day).Distinct().Count(),
        readings = p.Readings.Count,
        recognised = p.Recognised.Select(m => new { m.Column, m.Metric, m.Rows }),
        p.Ignored,
        p.Warnings,

        // A handful of parsed rows, so the user can see the importer read their dates
        // and numbers the way they meant them before committing anything.
        sample = p.Readings
            .OrderBy(r => r.Day)
            .Take(12)
            .Select(r => new { day = r.Day.ToString("yyyy-MM-dd"), r.Metric, value = Math.Round(r.Value, 2) }),

        written,
        committed = written is not null,
    };

    // Into the typed tables, merged rather than replaced.
    //
    // A day may already hold Oura data, and an Apple export that overwrote the row
    // would silently drop the ring's sleep stages to record a step count. Each field is
    // set only if the import actually carries it.
    private async Task<Dictionary<string, int>> WriteAsync(List<ImportedReading> readings)
    {
        var written = new Dictionary<string, int>();
        var byDay = readings.GroupBy(r => r.Day).OrderBy(g => g.Key).ToList();

        var from = byDay[0].Key;
        var to = byDay[^1].Key;

        var activity = (await repo.GetActivityAsync(from, to)).ToDictionary(a => a.Day);
        var sleep = (await repo.GetSleepAsync(from, to)).GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.First());

        var activityChanged = new List<DailyActivity>();
        var sleepChanged = new List<SleepSession>();

        foreach (var day in byDay)
        {
            var v = day.ToDictionary(r => r.Metric, r => r.Value);

            if (v.ContainsKey(MetricKeys.Steps) || v.ContainsKey(MetricKeys.ActiveCalories))
            {
                var row = activity.TryGetValue(day.Key, out var a)
                    ? a
                    : new DailyActivity { Id = day.Key.ToString("yyyy-MM-dd"), Day = day.Key };

                if (v.TryGetValue(MetricKeys.Steps, out var steps)) row.Steps = (int)Math.Round(steps);
                if (v.TryGetValue(MetricKeys.ActiveCalories, out var cal)) row.ActiveCalories = (int)Math.Round(cal);
                activityChanged.Add(row);
            }

            var sleepFields = new[]
            {
                MetricKeys.TotalSleepMinutes, MetricKeys.HrvRmssd,
                MetricKeys.RestingHeartRate, MetricKeys.BreathingRate, MetricKeys.Spo2Average,
            };

            if (sleepFields.Any(v.ContainsKey))
            {
                // Apple's export has no session id, so the day is the identity. An
                // existing Oura session for that day is updated in place rather than
                // joined by a second row, which would be counted as a separate night
                // by everything downstream.
                var row = sleep.TryGetValue(day.Key, out var s)
                    ? s
                    : new SleepSession
                    {
                        Id = $"healthkit-{day.Key:yyyy-MM-dd}",
                        Day = day.Key,
                        BedtimeEnd = day.Key.ToDateTime(new TimeOnly(7, 0)),
                    };

                if (v.TryGetValue(MetricKeys.TotalSleepMinutes, out var mins)) row.TotalSleepMinutes = (int)Math.Round(mins);
                if (v.TryGetValue(MetricKeys.HrvRmssd, out var hrv)) row.AvgHrv = hrv;
                if (v.TryGetValue(MetricKeys.RestingHeartRate, out var rhr)) row.LowestHr = rhr;
                if (v.TryGetValue(MetricKeys.BreathingRate, out var br)) row.AvgBreathingRate = br;
                if (v.TryGetValue(MetricKeys.Spo2Average, out var spo2)) row.AvgSpo2 = spo2;
                sleepChanged.Add(row);
            }

            if (v.TryGetValue(MetricKeys.WeightKg, out var kg))
            {
                // Id is the day string — one weigh-in per day, upsert semantics — so a
                // re-import of an overlapping export replaces rather than duplicates.
                await repo.UpsertWeighInAsync(new WeighIn
                {
                    Id = day.Key.ToString("yyyy-MM-dd"),
                    Day = day.Key,
                    WeightKg = kg,
                });
                written["weight"] = written.GetValueOrDefault("weight") + 1;
            }
        }

        if (activityChanged.Count > 0)
        {
            await repo.UpsertActivityAsync(activityChanged);
            written["activity"] = activityChanged.Count;
        }

        if (sleepChanged.Count > 0)
        {
            await repo.UpsertSleepAsync(sleepChanged);
            written["sleep"] = sleepChanged.Count;
        }

        // The medium tier -- blood pressure, glucose, pulse, VO2 max. These used to be
        // parsed and then reported as unstorable; Measurements is where they land now.
        //
        // No context is attached, deliberately. A spreadsheet does not record whether a
        // reading was seated or fasting, and inventing one would file every imported
        // blood pressure into a bucket it may not belong in. They go into the unsplit
        // baseline, which is the honest place for a reading whose conditions were not
        // recorded.
        string[] mediumTier =
        [
            MetricKeys.SystolicBp, MetricKeys.DiastolicBp, MetricKeys.Glucose,
            MetricKeys.Pulse, MetricKeys.Vo2Max, MetricKeys.WaistCircumferenceCm,
        ];

        var measurements = readings
            .Where(r => mediumTier.Contains(r.Metric))
            .Select(r => new Measurement
            {
                Metric = r.Metric,
                Value = r.Value,
                Unit = "",
                // Midday rather than midnight: the day is all the export gave, and a
                // midnight instant sits on the boundary where any timezone nudge moves
                // it to the day before.
                ObservedAtLocal = r.Day.ToDateTime(new TimeOnly(12, 0)),
                Day = r.Day,
                Tier = Tiers.Medium,
                Source = "apple_health",
            })
            .ToList();

        if (measurements.Count > 0)
            written["measurements"] = await repo.UpsertMeasurementsAsync(measurements);

        return written;
    }
}
