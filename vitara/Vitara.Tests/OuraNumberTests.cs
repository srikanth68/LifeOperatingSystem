using System.Text.Json;
using Vitara.Infrastructure.Oura;

namespace Vitara.Tests;

// A whole-number field that arrives with a decimal point must not vanish.
//
// The client read every integer field through Convert.ChangeType on the raw JSON text, which
// throws on "312.4"; the exception was swallowed and the field came back null. Oura sends a
// workout's calories and distance as decimals, so every Oura workout was stored with no calories
// and no distance, and nothing anywhere said so.
public class OuraNumberTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    private const string Workout = """
    {
      "id": "w-1",
      "day": "2026-10-08",
      "activity": "running",
      "start_datetime": "2026-10-08T07:05:00-04:00",
      "end_datetime": "2026-10-08T07:36:00-04:00",
      "calories": 312.4,
      "distance": 5023.7,
      "intensity": "moderate",
      "label": null,
      "source": "autodetected"
    }
    """;

    [Fact]
    public void Workout_calories_and_distance_survive_a_decimal_point()
    {
        var w = OuraClient.MapWorkout(Json(Workout));

        Assert.Equal(312, w.Calories);
        Assert.Equal(5024, w.Distance);
    }

    [Fact]
    public void A_duration_sent_as_a_decimal_is_still_read()
    {
        var s = OuraClient.MapSleep(Json("""
        {
          "id": "s", "day": "2026-10-08",
          "bedtime_start": "2026-10-07T23:10:00-04:00", "bedtime_end": "2026-10-08T07:20:00-04:00",
          "total_sleep_duration": 28800.0, "lowest_heart_rate": 54.0
        }
        """));

        Assert.Equal(480, s.TotalSleepMinutes);
        Assert.Equal(54, s.LowestHr);
    }

    [Fact]
    public void Text_that_is_not_a_number_is_still_missing_rather_than_zero()
    {
        var w = OuraClient.MapWorkout(Json("""{ "id": "w", "day": "2026-10-08", "activity": "walking", "calories": "lots" }"""));

        Assert.Null(w.Calories);
    }
}
