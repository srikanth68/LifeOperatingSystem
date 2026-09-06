using System.Text.Json;
using Vitara.Infrastructure.Oura;

namespace Vitara.Tests;

// Parsing Oura's JSON is the most fragile code in the module: it is the one place a
// third party can change something and break Vitara silently. SafeSync catches the
// exception, logs a warning, and the sync carries on looking successful -- so a field
// rename shows up as data that quietly stops arriving.
//
// The payloads below follow the v2 API shapes the client reads. They exist to pin the
// mapping, and to make sure a missing optional field degrades rather than throws.
public class OuraParsingTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    // ── Sleep ──

    private const string FullSleep = """
    {
      "id": "sess-1",
      "day": "2026-09-01",
      "bedtime_start": "2026-08-31T23:12:00-05:00",
      "bedtime_end": "2026-09-01T07:04:00-05:00",
      "total_sleep_duration": 25200,
      "rem_sleep_duration": 5400,
      "deep_sleep_duration": 4800,
      "light_sleep_duration": 15000,
      "awake_time": 1200,
      "average_hrv": 48.5,
      "lowest_heart_rate": 51,
      "average_breath": 14.2,
      "average_spo2": 96.5,
      "skin_temp_deviation": -0.2
    }
    """;

    [Fact]
    public void SleepDurationsAreConvertedFromSecondsToMinutes()
    {
        // Oura reports seconds. Everything stored is minutes, and getting this backwards
        // would put a seven-hour night on record as 25,200 minutes.
        var s = OuraClient.MapSleep(Json(FullSleep));

        Assert.Equal(420, s.TotalSleepMinutes);   // 25200s = 7h
        Assert.Equal(90, s.RemMinutes);
        Assert.Equal(80, s.DeepMinutes);
        Assert.Equal(250, s.LightMinutes);
        Assert.Equal(20, s.AwakeMinutes);
    }

    [Fact]
    public void SleepIdentityAndMetricsRoundTrip()
    {
        var s = OuraClient.MapSleep(Json(FullSleep));

        Assert.Equal("sess-1", s.Id);
        Assert.Equal(new DateOnly(2026, 9, 1), s.Day);
        Assert.Equal(48.5, s.AvgHrv);
        Assert.Equal(51, s.LowestHr);
        Assert.Equal(96.5, s.AvgSpo2);
        Assert.Equal(-0.2, s.SkinTempDeviation);
    }

    [Fact]
    public void SleepScoreIsNormallyAbsentOnThisEndpoint()
    {
        // It lives on daily_sleep and is merged in afterwards. Null here is correct, and
        // a test guards against someone "fixing" it to zero -- which would read as the
        // worst possible night rather than as unknown.
        Assert.Null(OuraClient.MapSleep(Json(FullSleep)).Score);
    }

    [Fact]
    public void AMinimalSleepRecordDoesNotThrow()
    {
        // Oura omits optional fields on partial nights. Losing the whole sync to one
        // short nap would be a bad trade.
        var s = OuraClient.MapSleep(Json("""
        {
          "id": "nap-1",
          "day": "2026-09-01",
          "bedtime_start": "2026-09-01T13:00:00-05:00",
          "bedtime_end": "2026-09-01T13:40:00-05:00"
        }
        """));

        Assert.Equal("nap-1", s.Id);
        Assert.Equal(0, s.TotalSleepMinutes);
        Assert.Null(s.AvgHrv);
        Assert.Null(s.AvgSpo2);
    }

    // ── Readiness ──

    private const string ReadinessItem = """
    { "id": "r-1", "day": "2026-09-01", "score": 78 }
    """;

    private const string Contributors = """
    {
      "hrv_balance": 72,
      "recovery_index": 88,
      "resting_heart_rate": 64,
      "activity_balance": 70,
      "sleep_balance": 81,
      "temperature_deviation": 1
    }
    """;

    [Fact]
    public void ReadinessContributorsAreLifted()
    {
        var r = OuraClient.MapReadiness(Json(ReadinessItem), Json(Contributors));

        Assert.Equal("r-1", r.Id);
        Assert.Equal(78, r.Score);
        Assert.Equal(72, r.HrvBalance);
        Assert.Equal(64, r.RestingHeartRate);
        Assert.Equal(81, r.SleepBalance);
    }

    [Fact]
    public void ReadinessSurvivesMissingContributors()
    {
        // The block is absent on days the ring was not worn enough.
        var r = OuraClient.MapReadiness(Json(ReadinessItem), null);

        Assert.Equal(78, r.Score);
        Assert.Null(r.HrvBalance);
        Assert.Null(r.RestingHeartRate);
    }

    [Theory]
    // The level is derived here, not sent by Oura, so the thresholds are ours to keep.
    [InlineData(92, "optimal")]
    [InlineData(85, "optimal")]
    [InlineData(84, "good")]
    [InlineData(70, "good")]
    [InlineData(69, "pay_attention")]
    [InlineData(20, "pay_attention")]
    public void ReadinessLevelFollowsTheScore(int score, string expected)
    {
        var r = OuraClient.MapReadiness(Json($$"""{ "id": "x", "day": "2026-09-01", "score": {{score}} }"""), null);
        Assert.Equal(expected, r.Level);
    }

    [Fact]
    public void AMissingScoreIsNotTreatedAsAGoodDay()
    {
        // A null score falls through the switch to "pay_attention". Worth pinning: the
        // alternative reading -- absent means fine -- would hide exactly the days the
        // ring failed to record.
        var r = OuraClient.MapReadiness(Json("""{ "id": "x", "day": "2026-09-01" }"""), null);

        Assert.Null(r.Score);
        Assert.Equal("pay_attention", r.Level);
    }
}
