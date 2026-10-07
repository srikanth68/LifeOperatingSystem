using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Vitara.API.Controllers;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Tests;

// A resting heart rate has to be a heart rate.
//
// Oura's readiness payload carries a `contributors.resting_heart_rate`, and it is not a
// pulse -- every contributor is a score out of 100, where 100 means that input helped
// today's readiness as much as it could, which for a resting heart rate happens when it
// is LOW. It was read as bpm, served as `restingHr`, labelled bpm on two tabs and pushed
// through the biological-age formula as a pulse. The user saw a resting heart rate of
// 100 on a morning their actual overnight low was in the fifties, and the bio-age model
// had been adding years for being in good shape.
//
// The genuine figure is the night's lowest heart rate, which is what the analysis layer
// has always projected. These tests hold every surface to that one source.
public class RestingHeartRateTests
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly DateOnly Today = LocalTime.Today;

    // The shape that caused it: a contributor score of 100 beside a real pulse of 52.
    private static (FakeRepo Repo, DateOnly Day) PerfectScoreLowPulse(int days = 30)
    {
        var repo = new FakeRepo();

        for (var i = 0; i < days; i++)
        {
            var day = Today.AddDays(-i);

            repo.SleepData.Add(new SleepSession
            {
                Id = $"s{i}", Day = day,
                BedtimeStart = day.ToDateTime(new TimeOnly(23, 0)).AddDays(-1),
                BedtimeEnd = day.ToDateTime(new TimeOnly(7, 0)),
                TotalSleepMinutes = 430, DeepMinutes = 70, RemMinutes = 95, LightMinutes = 265,
                Score = 80, AvgHrv = 48, LowestHr = 52,
            });

            repo.ReadinessData.Add(new DailyReadiness
            {
                Id = $"r{i}", Day = day, Score = 85, Level = "optimal",
                HrvBalance = 90, RestingHrContributor = 100,
            });
        }

        return (repo, Today);
    }

    [Fact]
    public async Task The_dashboard_reports_the_pulse_not_the_contributor()
    {
        var (repo, _) = PerfectScoreLowPulse();

        var ok = Assert.IsType<OkObjectResult>(await new DashboardController(repo).Get());
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;

        var restingHr = json.GetProperty("readiness").GetProperty("restingHr").GetDouble();

        Assert.Equal(52, restingHr);
        // And the score is still available, under a name that cannot be mistaken for one.
        Assert.Equal(100, json.GetProperty("readiness").GetProperty("restingHrContributor").GetInt32());
    }

    [Fact]
    public async Task The_weekly_average_is_an_average_of_pulses()
    {
        var (repo, _) = PerfectScoreLowPulse();

        var ok = Assert.IsType<OkObjectResult>(await new DashboardController(repo).Get());
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;

        // Otherwise the tile compares tonight's 52 against a "usual" of 100 and reports
        // a 48-point improvement every single morning.
        Assert.Equal(52, json.GetProperty("weeklyAvg").GetProperty("rhr").GetDouble());
    }

    [Fact]
    public async Task The_readiness_rows_carry_a_pulse_for_the_tab_that_draws_bpm()
    {
        var (repo, _) = PerfectScoreLowPulse();

        var ok = Assert.IsType<OkObjectResult>(await new ReadinessController(repo).Get(14));
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;

        var first = json.EnumerateArray().First();
        Assert.Equal(52, first.GetProperty("restingHeartRate").GetDouble());
        Assert.Equal(100, first.GetProperty("restingHrContributor").GetInt32());
    }

    [Fact]
    public async Task A_night_the_ring_missed_leaves_the_pulse_empty_rather_than_guessed()
    {
        var repo = new FakeRepo();
        repo.ReadinessData.Add(new DailyReadiness
        {
            Id = "r0", Day = Today, Score = 85, Level = "optimal", RestingHrContributor = 96,
        });

        var ok = Assert.IsType<OkObjectResult>(await new ReadinessController(repo).Get(14));
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;

        var first = json.EnumerateArray().First();
        Assert.Equal(JsonValueKind.Null, first.GetProperty("restingHeartRate").ValueKind);
    }

    [Fact]
    public async Task Biological_age_is_not_aged_by_a_good_resting_heart_rate()
    {
        // The formula is (rhr - 65) / 3 years. Read as a pulse, a contributor score of
        // 100 added nearly twelve years to someone whose heart was doing well -- the
        // sign inverted, so the better the reading the older the answer.
        var (repo, _) = PerfectScoreLowPulse();

        var ok = Assert.IsType<OkObjectResult>(await new BioAgeController(repo).Get());
        var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;

        var factors = json.GetProperty("factors");
        var rhr = factors.GetProperty("restingHrScore").GetDouble();

        Assert.Equal(52, rhr);
    }

    [Fact]
    public void The_parser_keeps_calling_it_a_contributor()
    {
        // The name is the whole fix. A future reader of MapReadiness has to be told that
        // Oura's field name is misleading, or this comes back.
        var item = JsonDocument.Parse("""{"id":"r-1","day":"2026-09-01","score":78}""").RootElement;
        var contributors = JsonDocument.Parse("""{"resting_heart_rate":100,"hrv_balance":72}""").RootElement;

        var readiness = Vitara.Infrastructure.Oura.OuraClient.MapReadiness(item, contributors);

        Assert.Equal(100, readiness.RestingHrContributor);
    }
}
