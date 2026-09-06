using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;

namespace Vitara.Worker;

// Pulls the food diary in daily, so nutrition stops being something to type in by hand.
//
// Vitara has always had DailyNutrition, MealEntry and a USDA food search, and nothing
// ever populated them automatically. This closes that, using the MyFitnessPal sidecar.
//
// It re-reads the last few days rather than only yesterday. Food gets logged late and
// edited afterwards far more than ring data does -- an entry added on Tuesday for
// Sunday is normal -- so a window that only looked at yesterday would miss most
// corrections.
//
// Everything here follows the same discipline as the Oura fixes: the last-synced time
// moves only when rows were actually written, failures are recorded rather than logged
// and forgotten, and an unreachable source never overwrites real data with zeros.
public class NutritionSyncWorker(IServiceProvider services, ILogger<NutritionSyncWorker> logger) : BackgroundService
{
    public const string SourceName = "mfp";

    private static readonly TimeSpan Interval = TimeSpan.FromHours(
        double.TryParse(Environment.GetEnvironmentVariable("NUTRITION_SYNC_HOURS"), out var h) && h > 0 ? h : 6);

    // Offset from the Oura sync so the two are not competing for the same minute.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(4);

    private static readonly int WindowDays =
        int.TryParse(Environment.GetEnvironmentVariable("NUTRITION_SYNC_DAYS"), out var d) && d > 0 ? Math.Min(d, 14) : 3;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using (var probe = services.CreateScope())
        {
            var source = probe.ServiceProvider.GetRequiredService<INutritionSource>();
            if (!source.IsConfigured)
            {
                // Says it once and stops. A daily complaint about a feature nobody
                // turned on is exactly the kind of noise that trains people to ignore
                // logs and notifications alike.
                logger.LogInformation("Nutrition sync disabled (MFP_API_URL not set).");
                return;
            }
        }

        logger.LogInformation("Nutrition sync worker started. Every {h}h over a {d}-day window.",
            Interval.TotalHours, WindowDays);

        try { await Task.Delay(StartupDelay, ct); } catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await SyncAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Nutrition sync failed."); }

            try { await Task.Delay(Interval, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task SyncAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IVitaraRepository>();
        var source = scope.ServiceProvider.GetRequiredService<INutritionSource>();

        var state = await repo.GetSyncStateAsync(SourceName) ?? new SyncState { Source = SourceName };
        state.LastAttemptAt = DateTime.UtcNow;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var failures = new List<string>();
        var daysWritten = 0;
        var mealsWritten = 0;

        for (var i = 0; i < WindowDays; i++)
        {
            var day = today.AddDays(-i);
            var diary = await source.GetDiaryAsync(day, ct);

            if (diary is null)
            {
                // Could not fetch. Critically NOT the same as an empty diary: writing
                // zeros here would record "ate nothing" as a fact about a day the
                // network simply failed on.
                failures.Add(day.ToString("MM-dd"));
                continue;
            }

            mealsWritten += await repo.ReplaceMealsForDayAsync(day, SourceName, diary.Entries.Select(e => new MealEntry
            {
                Day = day,
                MealType = NormaliseMeal(e.Meal),
                FoodName = e.Name,
                ServingQty = e.Quantity ?? 1,
                ServingUnit = e.Unit,
                Calories = e.Calories ?? 0,
                Protein = e.Protein ?? 0,
                Carbs = e.Carbs ?? 0,
                Fat = e.Fat ?? 0,
                Fiber = e.Fiber,
            }));

            // A day with no entries still updates the totals to zero, because that IS
            // what the source says about it -- the distinction being guarded above is
            // between an empty answer and no answer at all.
            await repo.UpsertNutritionAsync([new DailyNutrition
            {
                Id = day.ToString("yyyy-MM-dd"),
                Day = day,
                Calories = (int)Math.Round(diary.Totals.Calories ?? 0),
                Protein = diary.Totals.Protein ?? 0,
                Carbs = diary.Totals.Carbs ?? 0,
                Fat = diary.Totals.Fat ?? 0,
                Fiber = diary.Totals.Fiber,
                Sugar = diary.Totals.Sugar,
                Sodium = diary.Totals.Sodium,
                CalorieGoal = diary.Goals.Calories is { } c ? (int)Math.Round(c) : null,
                ProteinGoal = diary.Goals.Protein,
                CarbGoal = diary.Goals.Carbs,
                FatGoal = diary.Goals.Fat,
            }]);

            daysWritten++;
        }

        // Only on a real write, for the same reason the Oura token's was fixed: a
        // timestamp that moves on every attempt cannot tell a working sync from a
        // broken one, and the UI believes it either way.
        if (daysWritten > 0) state.LastSyncedAt = DateTime.UtcNow;
        state.LastError = failures.Count == 0 ? null : $"Could not fetch: {string.Join(", ", failures)}";
        await repo.SaveSyncStateAsync(state);

        if (daysWritten > 0)
            logger.LogInformation("Nutrition sync: {Days} days, {Meals} entries.{Fail}",
                daysWritten, mealsWritten, failures.Count == 0 ? "" : $" Failed: {string.Join(", ", failures)}");
        else
            logger.LogWarning("Nutrition sync wrote nothing — {Fail}", state.LastError ?? "no data returned");
    }

    // MyFitnessPal's meal names are user-editable and locale-dependent; Vitara stores a
    // small fixed set. Anything unrecognised becomes a snack rather than being dropped,
    // because the food matters more than which bucket it sits in.
    private static string NormaliseMeal(string meal) => meal.Trim().ToLowerInvariant() switch
    {
        "breakfast" => "breakfast",
        "lunch" => "lunch",
        "dinner" => "dinner",
        _ => "snack",
    };
}
