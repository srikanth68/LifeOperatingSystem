using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Vitara.Application;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.API.Controllers;

public record XmlCommitRequest(string Token, List<string> Sources, bool SetHeight);

// Importing Apple Health's own export.xml.
//
// UPLOADED ONCE, PARSED ONCE. The real file is 810MB, and the obvious two-call shape --
// preview then commit, each taking the file -- would send it over the mesh twice. So
// the upload parses straight to daily aggregates, keeps those (a few hundred kilobytes)
// under a token, and the commit works from them. The user still sees everything before
// anything is written; only the file stops making the trip.
//
// THE SOURCE PICKER IS THE POINT. Oura writes into Apple Health, and Oura also reaches
// Vitara through its own API with sleep stages, RMSSD and a temperature deviation that
// Apple never receives. In the real export Oura was the single largest writer at
// 714,987 records. Importing it would overwrite the better record with a coarser copy
// of itself, so it is listed, counted, and left unticked -- visible rather than
// silently filtered, because which copy to keep is the user's call.
[ApiController, Route("api/healthimport/xml")]
public class HealthXmlController(IVitaraRepository repo, ILogger<HealthXmlController> logger) : ControllerBase
{
    // Staged aggregates live beside the database, inside the data volume, so a restart
    // between upload and commit does not lose a twenty-minute parse.
    private static string StagingDir =>
        Path.Combine(Directory.GetCurrentDirectory(), "..", "import-staging");

    // Long enough to read the summary and decide, short enough that an abandoned
    // upload does not sit in the volume forever.
    private static readonly TimeSpan StagingTtl = TimeSpan.FromHours(6);

    [HttpPost]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
    public async Task<IActionResult> Upload(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Attach export.xml from your Apple Health export." });

        Directory.CreateDirectory(StagingDir);
        Sweep();

        XmlScanResult scan;
        var started = DateTime.UtcNow;

        try
        {
            await using var stream = file.OpenReadStream();
            scan = HealthXmlImport.Scan(stream, file.Length);
        }
        catch (Exception ex)
        {
            // export_cda.xml, a zip, or a truncated download all land here. A message
            // beats a 500 with a stack trace in the browser.
            logger.LogWarning(ex, "Apple Health XML could not be read ({File}).", file.FileName);
            return BadRequest(new { error = $"Could not read that file: {ex.Message}" });
        }

        if (scan.Daily.Count == 0)
            return Ok(Describe(scan, token: null, written: null));

        var token = Guid.NewGuid().ToString("N");
        await System.IO.File.WriteAllTextAsync(
            Path.Combine(StagingDir, $"{token}.json"),
            JsonSerializer.Serialize(new Staged(scan.Daily, scan.HeightMetres)));

        logger.LogInformation(
            "Apple Health XML: {MB}MB, {Seen:N0} records, {Mapped:N0} mapped, {Days} days, parsed in {Sec}s.",
            file.Length / 1024 / 1024, scan.RecordsSeen, scan.RecordsMapped,
            scan.Daily.Select(d => d.Day).Distinct().Count(),
            (int)(DateTime.UtcNow - started).TotalSeconds);

        return Ok(Describe(scan, token, written: null));
    }

    [HttpPost("commit")]
    public async Task<IActionResult> Commit([FromBody] XmlCommitRequest req)
    {
        var path = Path.Combine(StagingDir, $"{Sanitise(req.Token)}.json");
        if (!System.IO.File.Exists(path))
            return BadRequest(new { error = "That upload has expired. Upload the file again." });

        var staged = JsonSerializer.Deserialize<Staged>(await System.IO.File.ReadAllTextAsync(path));
        if (staged is null) return BadRequest(new { error = "The staged upload could not be read." });

        var chosen = req.Sources?.ToHashSet(StringComparer.Ordinal) ?? [];
        var rows = staged.Daily.Where(d => chosen.Contains(d.Source)).ToList();

        if (rows.Count == 0)
            return Ok(new { written = new Dictionary<string, int>(), message = "No sources were selected." });

        var written = await WriteAsync(rows);

        if (req.SetHeight && staged.HeightMetres is { } h and > 0.5 and < 2.5)
        {
            // Height is the field BMI and waist-to-height both need, and it was empty.
            // Only ever set, never overwritten with a second guess.
            var profile = await repo.GetProfileAsync() ?? new UserProfile();
            if (profile.Height is null or <= 0)
            {
                profile.Height = Math.Round(h, 3);
                await repo.SaveProfileAsync(profile);
                written["height (profile)"] = 1;
            }
        }

        System.IO.File.Delete(path);

        logger.LogInformation("Apple Health XML committed: {Written}.",
            string.Join(", ", written.Select(w => $"{w.Key} {w.Value}")));

        return Ok(new { written, committed = true });
    }

    [HttpDelete("{token}")]
    public IActionResult Discard(string token)
    {
        var path = Path.Combine(StagingDir, $"{Sanitise(token)}.json");
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        return NoContent();
    }

    // ── Writing ─────────────────────────────────────────────────────────────────

    // Into the typed tables, merged field by field, exactly as the spreadsheet importer
    // does. Imported data has to follow the same path Oura's does so the projector
    // picks it up -- an importer writing derived rows would bypass it entirely.
    private async Task<Dictionary<string, int>> WriteAsync(List<XmlDailyValue> rows)
    {
        var written = new Dictionary<string, int>();
        var byDay = rows.GroupBy(r => r.Day).OrderBy(g => g.Key).ToList();

        var from = byDay[0].Key;
        var to = byDay[^1].Key;

        var activity = (await repo.GetActivityAsync(from, to)).ToDictionary(a => a.Day);
        var sleep = (await repo.GetSleepAsync(from, to)).GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.First());

        var activityChanged = new List<DailyActivity>();
        var sleepChanged = new List<SleepSession>();
        var measurements = new List<Measurement>();

        foreach (var day in byDay)
        {
            // Several apps can report the same metric on the same day. The larger value
            // wins for counts -- one app tracking only part of the day should not
            // replace one that tracked all of it.
            var v = day.GroupBy(r => r.Metric).ToDictionary(g => g.Key, g => g.Max(r => r.Value));

            if (v.ContainsKey(MetricKeys.Steps) || v.ContainsKey(MetricKeys.ActiveCalories))
            {
                var row = activity.TryGetValue(day.Key, out var a)
                    ? a
                    : new DailyActivity { Id = day.Key.ToString("yyyy-MM-dd"), Day = day.Key };

                if (v.TryGetValue(MetricKeys.Steps, out var steps)) row.Steps = (int)Math.Round(steps);
                if (v.TryGetValue(MetricKeys.ActiveCalories, out var cal)) row.ActiveCalories = (int)Math.Round(cal);
                activityChanged.Add(row);
            }

            string[] sleepFields =
            [
                MetricKeys.TotalSleepMinutes, MetricKeys.RestingHeartRate,
                MetricKeys.BreathingRate, MetricKeys.Spo2Average,
            ];

            if (sleepFields.Any(v.ContainsKey))
            {
                var row = sleep.TryGetValue(day.Key, out var s)
                    ? s
                    : new SleepSession
                    {
                        Id = $"healthkit-{day.Key:yyyy-MM-dd}",
                        Day = day.Key,
                        BedtimeEnd = day.Key.ToDateTime(new TimeOnly(7, 0)),
                    };

                if (v.TryGetValue(MetricKeys.TotalSleepMinutes, out var mins)) row.TotalSleepMinutes = (int)Math.Round(mins);
                if (v.TryGetValue(MetricKeys.RestingHeartRate, out var rhr)) row.LowestHr = rhr;
                if (v.TryGetValue(MetricKeys.BreathingRate, out var br)) row.AvgBreathingRate = br;
                if (v.TryGetValue(MetricKeys.Spo2Average, out var spo2)) row.AvgSpo2 = spo2;

                // AvgHrv is deliberately NOT set from this import. Apple records SDNN,
                // that field holds Oura's RMSSD, and the two are different statistics --
                // writing one into the other would corrupt a real series with a
                // measurement it cannot be compared against.
                sleepChanged.Add(row);
            }

            foreach (var metric in new[]
                     {
                         MetricKeys.WeightKg, MetricKeys.Vo2Max, MetricKeys.SystolicBp,
                         MetricKeys.DiastolicBp, MetricKeys.WaistCircumferenceCm, HealthXmlImport.HrvSdnn,
                     })
            {
                if (!v.TryGetValue(metric, out var value)) continue;

                measurements.Add(new Measurement
                {
                    Metric = metric,
                    Value = value,
                    Unit = "",
                    // Midday, not midnight. The day is all the aggregate carries, and a
                    // midnight instant sits on the boundary where any timezone nudge
                    // moves it to the day before.
                    ObservedAtLocal = day.Key.ToDateTime(new TimeOnly(12, 0)),
                    Day = day.Key,
                    Tier = Tiers.Medium,
                    Source = "apple_health",
                });
            }
        }

        if (activityChanged.Count > 0)
        {
            await repo.UpsertActivityAsync(activityChanged);
            written["activity days"] = activityChanged.Count;
        }

        if (sleepChanged.Count > 0)
        {
            await repo.UpsertSleepAsync(sleepChanged);
            written["sleep nights"] = sleepChanged.Count;
        }

        if (measurements.Count > 0)
            written["measurements"] = await repo.UpsertMeasurementsAsync(measurements);

        return written;
    }

    // ── Plumbing ────────────────────────────────────────────────────────────────

    private sealed record Staged(List<XmlDailyValue> Daily, double? HeightMetres);

    private static object Describe(XmlScanResult s, string? token, Dictionary<string, int>? written) => new
    {
        token,
        megabytes = Math.Round(s.BytesRead / 1024.0 / 1024.0, 1),
        recordsSeen = s.RecordsSeen,
        recordsMapped = s.RecordsMapped,
        firstDay = s.FirstDay?.ToString("yyyy-MM-dd"),
        lastDay = s.LastDay?.ToString("yyyy-MM-dd"),
        days = s.Daily.Select(d => d.Day).Distinct().Count(),

        heightMetres = s.HeightMetres is { } h ? Math.Round(h, 3) : (double?)null,

        sources = s.Sources.Select(src => new
        {
            src.Source,
            src.Records,
            src.Days,
            src.Metrics,

            // Not a filter, a default. The row is shown with its real counts either way;
            // the tick is just pre-set to the answer that is right for most people.
            src.RecommendedOff,
        }),

        ignored = s.IgnoredTypes,
        warnings = s.Warnings,
        written,
    };

    // An abandoned upload must not sit in the data volume indefinitely, and the parse
    // is expensive enough that the window has to be generous.
    private static void Sweep()
    {
        foreach (var f in Directory.EnumerateFiles(StagingDir, "*.json"))
            try
            {
                if (DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(f) > StagingTtl)
                    System.IO.File.Delete(f);
            }
            catch (IOException) { /* in use, or already gone */ }
    }

    // The token reaches a file path, so it may only ever be the hex it was issued as.
    private static string Sanitise(string? token) =>
        new((token ?? "").Where(char.IsAsciiLetterOrDigit).Take(64).ToArray());
}
