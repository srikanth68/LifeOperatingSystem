using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Vitara.API.Controllers;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// The night is the longest session of the day, everywhere.
//
// Oura's sleep endpoint returns every period it detected: the night, naps, and short segments
// when a night is split. The dashboard took whichever session came LAST, so an afternoon nap of
// an hour and a half was shown as last night's sleep while Oura's own app said eight hours, and
// the HRV, resting heart rate and skin temperature on the home screen were the nap's. The sleep
// summary San reads, the bio-age estimate and the chronotype averaged naps in as if they were
// nights. Each of these now asks SleepNights for the night.
public class MainNightTests
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly DateOnly Today = LocalTime.Today;

    private static SleepSession Night(DateOnly day, string id = "night", double hrv = 52, double lowHr = 54, int score = 86) => new()
    {
        Id = id, Day = day,
        BedtimeStart = day.AddDays(-1).ToDateTime(new TimeOnly(23, 10)),
        BedtimeEnd = day.ToDateTime(new TimeOnly(7, 20)),
        TotalSleepMinutes = 480, DeepMinutes = 90, RemMinutes = 110, LightMinutes = 280, AwakeMinutes = 10,
        Score = score, AvgHrv = hrv, LowestHr = lowHr, SkinTempDeviation = 0.1,
    };

    // Same day, later: an afternoon nap. Its readings are not the night's.
    private static SleepSession Nap(DateOnly day, string id = "nap") => new()
    {
        Id = id, Day = day,
        BedtimeStart = day.ToDateTime(new TimeOnly(15, 0)),
        BedtimeEnd = day.ToDateTime(new TimeOnly(16, 40)),
        TotalSleepMinutes = 90, DeepMinutes = 10, RemMinutes = 20, LightMinutes = 60, AwakeMinutes = 10,
        Score = null, AvgHrv = 31, LowestHr = 63, SkinTempDeviation = 0.9,
    };

    private static JsonElement Json(IActionResult r) =>
        JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(r).Value, Opts)).RootElement;

    // ── The rule itself ──────────────────────────────────────────────────────────

    [Fact]
    public void The_longest_session_of_a_day_is_its_night_whatever_order_they_arrive_in()
    {
        var nights = SleepNights.MainPerDay([Nap(Today), Night(Today), Nap(Today.AddDays(-1), "nap2"), Night(Today.AddDays(-1), "n2")]);

        Assert.Equal(["n2", "night"], nights.Select(n => n.Id));
    }

    // ── Where it was wrong ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_home_screen_shows_the_night_not_the_nap_that_came_after_it()
    {
        var repo = new FakeRepo();
        repo.SleepData.AddRange([Night(Today), Nap(Today)]);      // the nap is last, as it was in the real database

        var sleep = Json(await new DashboardController(repo).Get()).GetProperty("sleep");

        Assert.Equal(480, sleep.GetProperty("totalMinutes").GetInt32());
        Assert.Equal(52, sleep.GetProperty("hrv").GetDouble());
    }

    [Fact]
    public async Task Resting_heart_rate_and_skin_temperature_come_from_the_night()
    {
        var repo = new FakeRepo();
        repo.SleepData.AddRange([Night(Today), Nap(Today)]);
        repo.ReadinessData.Add(new DailyReadiness { Id = "r", Day = Today, Score = 80 });

        var readiness = Json(await new DashboardController(repo).Get()).GetProperty("readiness");

        Assert.Equal(54, readiness.GetProperty("restingHr").GetDouble());
        Assert.Equal(0.1, readiness.GetProperty("tempDeviation").GetDouble(), 3);
    }

    [Fact]
    public async Task The_sleep_summary_counts_nights_and_averages_nights()
    {
        var repo = new FakeRepo();
        repo.SleepData.AddRange([Night(Today), Nap(Today), Night(Today.AddDays(-1), "n2"), Nap(Today.AddDays(-1), "nap2")]);

        var json = Json(await new SleepController(repo).Summary(7));

        Assert.Equal(2, json.GetProperty("count").GetInt32());
        Assert.Equal(52, json.GetProperty("avgHrv").GetDouble(), 3);
        Assert.Equal(480, json.GetProperty("avgTotalMin").GetDouble(), 3);
    }

    [Fact]
    public void A_nap_does_not_move_the_usual_bedtime()
    {
        var sessions = Enumerable.Range(0, 30)
            .SelectMany(i => new[] { Night(Today.AddDays(-i), $"n{i}"), Nap(Today.AddDays(-i), $"p{i}") })
            .ToList();

        var chronotype = Signature.WhenTheySleep(sessions)!;

        Assert.Equal(30, chronotype.Nights);
        Assert.InRange(chronotype.BedMinutes, -51, -49);                  // 23:10 is 50 minutes before midnight
    }

    [Fact]
    public async Task Bio_age_reads_the_nights_heart_rate_variability_not_the_naps()
    {
        var repo = new FakeRepo { Profile = new UserProfile { Id = "p", Age = 40 } };
        for (var i = 0; i < 14; i++)
        {
            var day = Today.AddDays(-i);
            repo.SleepData.AddRange([Night(day, $"n{i}", hrv: 60), Nap(day, $"p{i}")]);
            repo.ReadinessData.Add(new DailyReadiness { Id = $"r{i}", Day = day, Score = 80 });
        }

        var factors = Json(await new BioAgeController(repo).Get()).GetProperty("factors");

        Assert.Equal(60, factors.GetProperty("hrvScore").GetDouble(), 3);
    }
}
