using Vitara.Domain.Entities;
using Vitara.Infrastructure.Data;

namespace Vitara.Tests;

// Mirroring MyFitnessPal means replacing a day rather than merging it, because entries
// there are edited and deleted after the fact. Replacing a day is also the fastest way
// to destroy food the user logged by hand through San, which is what the source column
// exists to prevent.
public class NutritionSyncTests
{
    private static VitaraRepository Repo() => TestHelper.CreateFreshDb().repo;

    private static readonly DateOnly Day = new(2026, 9, 1);

    private static MealEntry Meal(string name, string source = "mfp", double kcal = 100) => new()
    {
        Day = Day, MealType = "lunch", FoodName = name, Source = source, Calories = kcal,
    };

    [Fact]
    public async Task ReplacingTheDayLeavesManualEntriesAlone()
    {
        // The failure this guards: a nightly sync silently deleting everything the user
        // logged by voice, because both kinds of row live in the same table.
        var repo = Repo();
        await repo.AddMealAsync(Meal("hand-logged omelette", source: "manual"));
        await repo.ReplaceMealsForDayAsync(Day, "mfp", [Meal("mfp toast")]);

        var meals = await repo.GetMealsAsync(Day);

        Assert.Equal(2, meals.Count);
        Assert.Contains(meals, m => m.FoodName == "hand-logged omelette" && m.Source == "manual");
        Assert.Contains(meals, m => m.FoodName == "mfp toast" && m.Source == "mfp");
    }

    [Fact]
    public async Task ASecondSyncReplacesTheFirstRatherThanDuplicatingIt()
    {
        var repo = Repo();
        await repo.ReplaceMealsForDayAsync(Day, "mfp", [Meal("porridge")]);
        await repo.ReplaceMealsForDayAsync(Day, "mfp", [Meal("porridge"), Meal("banana")]);

        var meals = await repo.GetMealsAsync(Day);

        Assert.Equal(2, meals.Count);
        Assert.Single(meals, m => m.FoodName == "porridge");
    }

    [Fact]
    public async Task DeletingAnEntryInMfpRemovesItHereToo()
    {
        // The reason this is a replace and not an upsert: an entry the user removed in
        // MyFitnessPal has to disappear, and merging would leave it behind forever.
        var repo = Repo();
        await repo.ReplaceMealsForDayAsync(Day, "mfp", [Meal("porridge"), Meal("banana")]);
        await repo.ReplaceMealsForDayAsync(Day, "mfp", [Meal("porridge")]);

        var meals = await repo.GetMealsAsync(Day);

        Assert.Single(meals);
        Assert.Equal("porridge", meals[0].FoodName);
    }

    [Fact]
    public async Task ClearingTheDayInMfpClearsOnlyMfpRows()
    {
        var repo = Repo();
        await repo.AddMealAsync(Meal("hand-logged", source: "manual"));
        await repo.ReplaceMealsForDayAsync(Day, "mfp", [Meal("toast")]);

        await repo.ReplaceMealsForDayAsync(Day, "mfp", []);

        var meals = await repo.GetMealsAsync(Day);
        Assert.Single(meals);
        Assert.Equal("manual", meals[0].Source);
    }

    [Fact]
    public async Task OtherDaysAreUntouched()
    {
        var repo = Repo();
        await repo.ReplaceMealsForDayAsync(Day.AddDays(-1), "mfp", [Meal("yesterday")]);
        await repo.ReplaceMealsForDayAsync(Day, "mfp", [Meal("today")]);

        Assert.Single(await repo.GetMealsAsync(Day.AddDays(-1)));
        Assert.Single(await repo.GetMealsAsync(Day));
    }

    [Fact]
    public async Task EntriesWrittenByASyncCarryItsSourceEvenIfTheCallerForgot()
    {
        // The worker builds MealEntry objects without setting Source; the repository is
        // the single place that decides it, so a future caller cannot accidentally
        // write rows a later sync will not clean up.
        var repo = Repo();
        await repo.ReplaceMealsForDayAsync(Day, "mfp", [new MealEntry { FoodName = "x", MealType = "snack" }]);

        var meal = Assert.Single(await repo.GetMealsAsync(Day));
        Assert.Equal("mfp", meal.Source);
        Assert.Equal(Day, meal.Day);
    }

    // ── Sync state ──

    [Fact]
    public async Task SyncStateRoundTripsAndUpdatesInPlace()
    {
        var repo = Repo();
        await repo.SaveSyncStateAsync(new SyncState { Source = "mfp", LastAttemptAt = DateTime.UtcNow, LastError = "boom" });
        await repo.SaveSyncStateAsync(new SyncState { Source = "mfp", LastSyncedAt = DateTime.UtcNow, LastAttemptAt = DateTime.UtcNow });

        var state = await repo.GetSyncStateAsync("mfp");

        Assert.NotNull(state);
        Assert.Null(state!.LastError);
        Assert.NotNull(state.LastSyncedAt);
    }

    [Fact]
    public async Task AnUnknownSourceIsNull()
        => Assert.Null(await Repo().GetSyncStateAsync("cronometer"));

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(5, true)]
    public void StalenessComesFromTheLastSuccessNotTheLastAttempt(int? daysAgo, bool expected)
    {
        var state = new SyncState
        {
            Source = "mfp",
            LastSyncedAt = daysAgo is null ? null : DateTime.UtcNow.AddDays(-daysAgo.Value),
            LastAttemptAt = DateTime.UtcNow,   // attempted just now either way
        };

        Assert.Equal(expected, state.IsStale);
    }
}
