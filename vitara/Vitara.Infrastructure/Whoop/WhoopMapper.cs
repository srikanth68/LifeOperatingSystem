using System.Globalization;
using System.Text.Json;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Infrastructure.Whoop;

// WHOOP's records, in Vitara's own tables (ADR 0029). Pure, so it is tested against WHOOP's
// documented shapes without a device.
//
// Into the SAME tables as Oura's, rows prefixed "whoop-", so every chart, baseline and
// detector reads them unchanged. That relies on one wearable per person, which is the
// realistic case and what the app asks for.
//
//   sleep (not naps)  -> SleepSession, labelled as the night (SleepNights.OuraNight), so the
//                        night/nap/Apple logic treats it exactly as it treats Oura's night
//   nap               -> SleepSession labelled "sleep", Oura's label for a nap
//   recovery          -> the night's HRV, resting heart rate, SpO2 and skin temperature on
//                        that SleepSession
//   workout           -> Workout, source "whoop" (a phone copy of the same session wins, ADR 0025)
//
// Not mapped, on purpose:
//   WHOOP's own scores (recovery, sleep performance): vendor scores are kept as sent, under
//     their own names, and never relabelled or used in Vitara's calculations (ADR 0003). Writing
//     recovery as "readiness" would put WHOOP's number in Oura's place in forecasts and baselines.
//     They belong in a "From your devices" area, which the local-first app adds (ADR 0030).
//   cycles (strain, kilojoules): strain is WHOOP's own 0-21 scale with no Vitara equivalent,
//     and its kilojoules are total energy, not active calories. Storing either as an Oura-like
//     field would be a number that looks comparable and is not.
//   steps: WHOOP's v2 API does not reliably return them; Apple Health does (ADR 0025).
public static class WhoopMapper
{
    public const string Source = "whoop";
    public const string IdPrefix = "whoop-";

    // The skin temperature WHOOP gives is absolute; Vitara's illness detector reads the
    // deviation from your own usual. The usual is the median of the window fetched, and
    // is not guessed from fewer than seven nights.
    private const int MinNightsForTempBaseline = 7;

    public static WhoopBundle Map(
        IReadOnlyList<JsonElement> sleeps, IReadOnlyList<JsonElement> recoveries, IReadOnlyList<JsonElement> workouts)
    {
        var bySleep = new Dictionary<string, JsonElement>();
        foreach (var r in recoveries)
            if (Str(r, "sleep_id") is { } sid && Scored(r)) bySleep[sid] = r;

        var temps = bySleep.Values
            .Select(r => Num(r.GetProperty("score"), "skin_temp_celsius"))
            .Where(t => t is not null).Select(t => t!.Value).OrderBy(t => t).ToList();
        double? usualTemp = temps.Count >= MinNightsForTempBaseline ? temps[temps.Count / 2] : null;

        var sessions = new List<SleepSession>();

        foreach (var s in sleeps)
        {
            if (!Scored(s) || Str(s, "id") is not { } id) continue;
            var offset = Str(s, "timezone_offset");
            var start = Local(Str(s, "start")!, offset);
            var end = Local(Str(s, "end")!, offset);
            var score = s.GetProperty("score");
            var stages = score.GetProperty("stage_summary");
            int Min(string field) => (int)Math.Round((Num(stages, field) ?? 0) / 60000.0);

            var light = Min("total_light_sleep_time_milli");
            var deep = Min("total_slow_wave_sleep_time_milli");
            var rem = Min("total_rem_sleep_time_milli");
            var nap = s.TryGetProperty("nap", out var n) && n.ValueKind == JsonValueKind.True;

            var session = new SleepSession
            {
                Id = IdPrefix + id,
                Day = DateOnly.FromDateTime(end),
                BedtimeStart = start,
                BedtimeEnd = end,
                TotalSleepMinutes = light + deep + rem,
                LightMinutes = light,
                DeepMinutes = deep,
                RemMinutes = rem,
                AwakeMinutes = Min("total_awake_time_milli"),
                AvgBreathingRate = Num(score, "respiratory_rate"),
                Type = nap ? "sleep" : SleepNights.OuraNight,
            };

            if (!nap && bySleep.TryGetValue(id, out var rec))
            {
                var rs = rec.GetProperty("score");
                session.AvgHrv = Num(rs, "hrv_rmssd_milli");
                session.LowestHr = Num(rs, "resting_heart_rate");
                session.AvgSpo2 = Num(rs, "spo2_percentage");
                if (Num(rs, "skin_temp_celsius") is { } t && usualTemp is { } u)
                    session.SkinTempDeviation = Math.Round(t - u, 2);
            }

            sessions.Add(session);
        }

        var mappedWorkouts = new List<Workout>();
        foreach (var w in workouts)
        {
            if (!Scored(w) || Str(w, "id") is not { } id) continue;
            var offset = Str(w, "timezone_offset");
            var start = Local(Str(w, "start")!, offset);
            var score = w.GetProperty("score");
            var strain = Num(score, "strain");
            mappedWorkouts.Add(new Workout
            {
                Id = IdPrefix + id,
                Day = DateOnly.FromDateTime(start),
                Activity = Title(Str(w, "sport_name") ?? "workout"),
                StartTime = start,
                EndTime = Local(Str(w, "end")!, offset),
                Calories = Num(score, "kilojoule") is { } kj ? (int)Math.Round(kj / 4.184) : null,
                Distance = Num(score, "distance_meter") is { } m ? (int)Math.Round(m) : null,
                // WHOOP's strain is 0-21; its own guidance calls 14 and over strenuous.
                Intensity = strain switch { >= 14 => "hard", >= 8 => "moderate", not null => "easy", _ => null },
                Source = Source,
            });
        }

        return new WhoopBundle(sessions, mappedWorkouts);
    }

    // The person's own wall clock at that moment, which is what every "bedtime" and "day"
    // here means. WHOOP gives the instant in UTC and the offset that applied.
    public static DateTime Local(string iso, string? offset)
    {
        var at = DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);
        if (offset is { Length: >= 3 } && (offset[0] is '+' or '-')
            && TimeSpan.TryParse(offset[1..], CultureInfo.InvariantCulture, out var ts))
            return at.ToOffset(offset[0] == '-' ? ts.Negate() : ts).DateTime;
        return at.LocalDateTime;
    }

    private static bool Scored(JsonElement e) =>
        Str(e, "score_state") == "SCORED" && e.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Object;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())
            : null;

    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static string Title(string sport) =>
        string.Join(' ', sport.Replace('_', ' ').Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
}
