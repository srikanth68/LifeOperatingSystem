using System.Text.Json;
using Vitara.Domain.Health;
using Vitara.Infrastructure.Profiles;
using Vitara.Infrastructure.Whoop;

namespace Vitara.Tests;

// WHOOP v2 records (ADR 0029), in the shapes WHOOP documents. Nobody here owns a strap, so
// these fixtures are the contract: if WHOOP's documented fields change, these change first.
public class WhoopMapperTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // A night in India (+05:30): asleep 23:10, awake 06:40 local.
    private static JsonElement Sleep(string id, string startUtc, string endUtc, bool nap = false, string state = "SCORED") => J($$"""
    {
      "id": "{{id}}", "cycle_id": 93845, "user_id": 10129, "start": "{{startUtc}}", "end": "{{endUtc}}",
      "timezone_offset": "+05:30", "nap": {{(nap ? "true" : "false")}}, "score_state": "{{state}}",
      "score": {
        "stage_summary": {
          "total_in_bed_time_milli": 27000000, "total_awake_time_milli": 1800000, "total_no_data_time_milli": 0,
          "total_light_sleep_time_milli": 12600000, "total_slow_wave_sleep_time_milli": 5400000,
          "total_rem_sleep_time_milli": 7200000, "sleep_cycle_count": 4, "disturbance_count": 6
        },
        "sleep_needed": { "baseline_milli": 27395716, "need_from_sleep_debt_milli": 352230, "need_from_recent_strain_milli": 208595, "need_from_recent_nap_milli": -12312 },
        "respiratory_rate": 15.2, "sleep_performance_percentage": 88, "sleep_consistency_percentage": 90, "sleep_efficiency_percentage": 93.3
      }
    }
    """);

    private static JsonElement Recovery(string sleepId, double temp, bool calibrating = false, double score = 72) => J($$"""
    {
      "cycle_id": 93845, "sleep_id": "{{sleepId}}", "user_id": 10129, "score_state": "SCORED",
      "score": { "user_calibrating": {{(calibrating ? "true" : "false")}}, "recovery_score": {{score}}, "resting_heart_rate": 54,
                 "hrv_rmssd_milli": 48.3, "spo2_percentage": 96.5, "skin_temp_celsius": {{temp}} }
    }
    """);

    private static readonly JsonElement Run = J("""
    {
      "id": "ecfc6a15-4661-442f-a9a4-f160dd7afae8", "user_id": 10129, "start": "2026-10-12T01:30:00.000Z",
      "end": "2026-10-12T02:15:00.000Z", "timezone_offset": "+05:30", "sport_name": "running", "score_state": "SCORED",
      "sport_id": 0,
      "score": { "strain": 11.2, "average_heart_rate": 148, "max_heart_rate": 176, "kilojoule": 1850.5,
                 "percent_recorded": 100, "distance_meter": 7400, "altitude_gain_meter": 40, "altitude_change_meter": 2 }
    }
    """);

    [Fact]
    public void ANightBecomesTheDaysSleepWithStagesInMinutesAndLocalTimes()
    {
        var b = WhoopMapper.Map([Sleep("s1", "2026-10-11T17:40:00.000Z", "2026-10-12T01:10:00.000Z")], [], []);

        var s = Assert.Single(b.Sleep);
        Assert.Equal("whoop-s1", s.Id);
        Assert.Equal(new DateOnly(2026, 10, 12), s.Day);                 // the day you woke
        Assert.Equal(new TimeOnly(23, 10), TimeOnly.FromDateTime(s.BedtimeStart));
        Assert.Equal(new TimeOnly(6, 40), TimeOnly.FromDateTime(s.BedtimeEnd));
        Assert.Equal(210, s.LightMinutes);
        Assert.Equal(90, s.DeepMinutes);
        Assert.Equal(120, s.RemMinutes);
        Assert.Equal(420, s.TotalSleepMinutes);                         // light + deep + REM, awake excluded
        Assert.Equal(30, s.AwakeMinutes);
        Assert.Equal(88, s.Score);
        Assert.Equal(15.2, s.AvgBreathingRate);
        Assert.Equal(SleepNights.OuraNight, s.Type);                    // read as the night, like Oura's
    }

    [Fact]
    public void TheNightsRecoveryCarriesHrvRestingHeartRateAndOxygen_AndTheScoreIsReadiness()
    {
        var b = WhoopMapper.Map([Sleep("s1", "2026-10-11T17:40:00Z", "2026-10-12T01:10:00Z")], [Recovery("s1", 33.4)], []);

        var s = Assert.Single(b.Sleep);
        Assert.Equal(48.3, s.AvgHrv);
        Assert.Equal(54, s.LowestHr);
        Assert.Equal(96.5, s.AvgSpo2);
        var r = Assert.Single(b.Readiness);
        Assert.Equal(72, r.Score);
        Assert.Equal("optimal", r.Level);                               // WHOOP green: 67 and over
        Assert.Equal(s.Day, r.Day);
    }

    [Fact]
    public void SkinTemperatureIsADeviationFromTheWindowsUsual_OnlyWithAWeekOfNights()
    {
        var nights = Enumerable.Range(1, 8).Select(i => Sleep($"s{i}", $"2026-10-{i:00}T17:40:00Z", $"2026-10-{i + 1:00}T01:10:00Z")).ToList();
        var recs = Enumerable.Range(1, 8).Select(i => Recovery($"s{i}", i == 8 ? 34.4 : 33.4)).ToList();

        var withWeek = WhoopMapper.Map(nights, recs, []);
        Assert.Equal(1.0, withWeek.Sleep.Single(s => s.Id == "whoop-s8").SkinTempDeviation);
        Assert.Equal(0.0, withWeek.Sleep.Single(s => s.Id == "whoop-s1").SkinTempDeviation);

        var tooFew = WhoopMapper.Map(nights.Take(3).ToList(), recs.Take(3).ToList(), []);
        Assert.All(tooFew.Sleep, s => Assert.Null(s.SkinTempDeviation));
    }

    [Fact]
    public void NapsAreNotTheNight_UnscoredAndCalibratingAreLeftOut()
    {
        var b = WhoopMapper.Map(
            [Sleep("nap", "2026-10-12T09:00:00Z", "2026-10-12T09:40:00Z", nap: true),
             Sleep("pending", "2026-10-12T17:40:00Z", "2026-10-13T01:10:00Z", state: "PENDING_SCORE"),
             Sleep("s1", "2026-10-11T17:40:00Z", "2026-10-12T01:10:00Z")],
            [Recovery("s1", 33.4, calibrating: true)], []);

        Assert.Equal("sleep", b.Sleep.Single(s => s.Id == "whoop-nap").Type);
        Assert.DoesNotContain(b.Sleep, s => s.Id == "whoop-pending");
        Assert.Empty(b.Readiness);                                      // WHOOP still calibrating
        Assert.Equal(48.3, b.Sleep.Single(s => s.Id == "whoop-s1").AvgHrv);   // the readings themselves still count
    }

    [Fact]
    public void AWorkoutKeepsItsLocalTimes_CaloriesFromKilojoules_IntensityFromStrain()
    {
        var w = Assert.Single(WhoopMapper.Map([], [], [Run]).Workouts);
        Assert.Equal("whoop-ecfc6a15-4661-442f-a9a4-f160dd7afae8", w.Id);
        Assert.Equal("Running", w.Activity);
        Assert.Equal(new DateOnly(2026, 10, 12), w.Day);
        Assert.Equal(new TimeOnly(7, 0), TimeOnly.FromDateTime(w.StartTime!.Value));
        Assert.Equal(442, w.Calories);                                  // 1850.5 kJ / 4.184
        Assert.Equal(7400, w.Distance);
        Assert.Equal("moderate", w.Intensity);
        Assert.Equal("whoop", w.Source);
    }

    [Theory]
    [InlineData("2026-10-12T01:10:00Z", "-04:00", 21, 10)]
    [InlineData("2026-10-12T01:10:00Z", "+05:30", 6, 40)]
    [InlineData("2026-10-12T01:10:00Z", "+00:00", 1, 10)]
    public void LocalTimeFollowsTheOffsetWhoopRecorded(string utc, string offset, int hour, int minute)
    {
        Assert.Equal(new TimeOnly(hour, minute), TimeOnly.FromDateTime(WhoopMapper.Local(utc, offset)));
    }

    [Fact]
    public void TheCallbackFilesTheTokenUnderThePersonInState()
    {
        Assert.True(ProfileResolver.ToWhoopState("default").Length >= 8);   // WHOOP's minimum
        var r = ProfileResolver.Resolve(null, null, ProfileResolver.ToWhoopState("mum"), "/api/whoop/callback");
        Assert.Equal("mum", r.Id);
        // Not read anywhere else.
        Assert.Equal("default", ProfileResolver.Resolve(null, null, ProfileResolver.ToWhoopState("mum"), "/api/sleep").Id);
    }
}
