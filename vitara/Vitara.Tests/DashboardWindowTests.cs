using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Vitara.API.Controllers;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Tests;

// A week is the wrong look-back for a metric published every few weeks.
//
// The dashboard asked for seven days of everything. Sleep and steps arrive daily, so
// that was right for them; cardiovascular age and VO2 max arrive every few weeks and
// resilience is built from weeks, so those three came back empty most of the time and
// the tab reported "needs more wear" to someone holding a perfectly good reading. The
// Body tab, asking for thirty days, showed that same reading at that same moment --
// two tabs of one app disagreeing about whether a number exists.
public class DashboardWindowTests
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly DateOnly Today = LocalTime.Today;

    private static FakeRepo WithSlowReadings(int daysAgo)
    {
        var repo = new FakeRepo();
        var day = Today.AddDays(-daysAgo);

        repo.CvAgeData.Add(new DailyCardiovascularAge { Id = "cv", Day = day, VascularAge = 38 });
        repo.Vo2Data.Add(new Vo2MaxRecord { Id = "vo", Day = day, Vo2Max = 42.6 });
        repo.ResilienceData.Add(new DailyResilience { Id = "re", Day = day, Level = "solid", SleepRecovery = 71 });

        return repo;
    }

    private static async Task<JsonElement> Dashboard(FakeRepo repo)
    {
        var ok = Assert.IsType<OkObjectResult>(await new DashboardController(repo).Get());
        return JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Opts)).RootElement;
    }

    [Fact]
    public async Task A_fortnight_old_cardiovascular_age_is_still_reported()
    {
        var json = await Dashboard(WithSlowReadings(14));

        Assert.Equal(38, json.GetProperty("cardiovascularAge").GetDouble());
        Assert.Equal(14, json.GetProperty("cardiovascularAgeDaysAgo").GetInt32());
    }

    [Fact]
    public async Task A_fortnight_old_vo2_max_is_still_reported()
    {
        var json = await Dashboard(WithSlowReadings(14));

        Assert.Equal(42.6, json.GetProperty("vo2Max").GetDouble(), 3);
        Assert.Equal(14, json.GetProperty("vo2MaxDaysAgo").GetInt32());
    }

    [Fact]
    public async Task Resilience_from_three_weeks_ago_is_still_reported()
    {
        var json = await Dashboard(WithSlowReadings(21));

        Assert.Equal("solid", json.GetProperty("resilience").GetProperty("level").GetString());
        Assert.Equal(21, json.GetProperty("resilience").GetProperty("daysAgo").GetInt32());
    }

    // The age travels with the value, always. Without it a reading from last month is
    // indistinguishable from this morning's, which is the failure this payload was
    // already fixed for once.
    [Fact]
    public async Task The_age_of_a_slow_reading_is_always_carried()
    {
        var json = await Dashboard(WithSlowReadings(3));

        Assert.Equal(3, json.GetProperty("cardiovascularAgeDaysAgo").GetInt32());
        Assert.Equal(Today.AddDays(-3).ToString("yyyy-MM-dd"), json.GetProperty("cardiovascularAgeDay").GetString());
    }

    [Fact]
    public async Task Nothing_recorded_is_null_rather_than_a_guess()
    {
        var json = await Dashboard(new FakeRepo());

        Assert.Equal(JsonValueKind.Null, json.GetProperty("cardiovascularAge").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("cardiovascularAgeDaysAgo").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("vo2Max").ValueKind);
    }

    // Beyond the window they genuinely are gone, and saying so is correct. The point of
    // the change is where the line sits, not that there is no line.
    [Fact]
    public async Task Something_from_last_year_is_not_dragged_forward()
    {
        var json = await Dashboard(WithSlowReadings(200));

        Assert.Equal(JsonValueKind.Null, json.GetProperty("cardiovascularAge").ValueKind);
    }
}
