using System.Text.Json;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Infrastructure.Oura;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// The night is what the source says it is.
//
// Oura labels every period it sends: long_sleep (the night), sleep, late_nap, rest, deleted.
// Vitara threw the label away and had to guess, first by "the last one" (which showed a nap as
// last night) and then by "the longest". Apple Health has no such label, so an Apple night is
// one Vitara assembled itself from the night's stages, and it is marked as that.
//
// Two rules follow. The night: Oura's long_sleep, else an Apple night, else the longest real
// sleep (only rows synced before the label was stored). A day's total: one source only. An
// Oura day sums Oura's periods; an Apple day is Apple's night; the two are never added.
public class SleepTypeTests
{
    private static readonly DateOnly Day = new(2026, 10, 9);

    private static SleepSession S(string id, int minutes, string? type, double? hrv = null) => new()
    {
        Id = id, Day = Day, Type = type, TotalSleepMinutes = minutes, AvgHrv = hrv,
        BedtimeEnd = Day.ToDateTime(new TimeOnly(7, 0)),
    };

    private static double Total(IEnumerable<Observation> obs) =>
        obs.Single(o => o.Metric == MetricKeys.TotalSleepMinutes).Value;

    // ── Reading the label ────────────────────────────────────────────────────────

    [Fact]
    public void Ouras_label_is_stored_with_the_session()
    {
        var s = OuraClient.MapSleep(JsonDocument.Parse("""
        { "id": "a", "day": "2026-10-09", "type": "late_nap",
          "bedtime_start": "2026-10-09T15:00:00-04:00", "bedtime_end": "2026-10-09T16:30:00-04:00" }
        """).RootElement);

        Assert.Equal("late_nap", s.Type);
    }

    // ── The night ────────────────────────────────────────────────────────────────

    [Fact]
    public void Ouras_night_wins_even_when_another_period_is_longer()
    {
        // A long lie-in Oura filed as a separate "sleep" period is not the night it scored.
        var night = SleepNights.Main([S("short-night", 360, "long_sleep"), S("lie-in", 400, "sleep")]);

        Assert.Equal("short-night", night.Id);
    }

    [Fact]
    public void A_deleted_period_is_never_the_night()
    {
        var night = SleepNights.Main([S("gone", 600, "deleted"), S("nap", 90, "late_nap")]);

        Assert.Equal("nap", night.Id);
    }

    [Fact]
    public void Ouras_night_beats_an_apple_night_for_the_same_day()
    {
        // Oura's carries stages and HRV; Apple's assembled night does not.
        var night = SleepNights.Main([S("apple", 500, SleepNights.AppleNight), S("oura", 470, "long_sleep")]);

        Assert.Equal("oura", night.Id);
    }

    [Fact]
    public void An_apple_night_is_the_night_when_oura_has_none()
    {
        var night = SleepNights.Main([S("nap", 90, "late_nap"), S("apple", 430, SleepNights.AppleNight)]);

        Assert.Equal("apple", night.Id);
    }

    [Fact]
    public void Rows_from_before_the_label_was_stored_fall_back_to_the_longest()
    {
        var night = SleepNights.Main([S("old-nap", 90, null), S("old-night", 470, null)]);

        Assert.Equal("old-night", night.Id);
    }

    [Fact]
    public void Unlabelled_oura_nights_ask_for_a_refetch_and_apple_rows_do_not()
    {
        Assert.True(SleepNights.NeedsLabels([S("oura-old", 470, null)]));
        Assert.False(SleepNights.NeedsLabels([S("oura", 470, "long_sleep"), S("healthkit-2026-10-09", 480, null)]));
    }

    // ── A day's total ────────────────────────────────────────────────────────────

    [Fact]
    public void Ouras_night_and_nap_add_up_but_apples_copy_of_the_night_does_not()
    {
        // Oura writes its night to Apple Health too; counting both doubled the night.
        var obs = ObservationProjector.FromSleep(
            [S("oura", 470, "long_sleep"), S("nap", 40, "late_nap"), S("apple", 480, SleepNights.AppleNight)]);

        Assert.Equal(510, Total(obs));
    }

    [Fact]
    public void Deleted_and_rest_periods_are_not_sleep()
    {
        var obs = ObservationProjector.FromSleep(
            [S("oura", 470, "long_sleep"), S("gone", 300, "deleted"), S("rest", 60, "rest")]);

        Assert.Equal(470, Total(obs));
    }

    [Fact]
    public void An_apple_only_day_is_apples_night()
    {
        var obs = ObservationProjector.FromSleep([S("apple", 430, SleepNights.AppleNight)]);

        Assert.Equal(430, Total(obs));
    }

    [Fact]
    public void Readings_come_from_the_night_not_from_apples_copy()
    {
        var obs = ObservationProjector.FromSleep([S("apple", 480, SleepNights.AppleNight), S("oura", 470, "long_sleep", hrv: 52)]);

        Assert.Equal(52, obs.Single(o => o.Metric == MetricKeys.HrvRmssd).Value);
    }
}
