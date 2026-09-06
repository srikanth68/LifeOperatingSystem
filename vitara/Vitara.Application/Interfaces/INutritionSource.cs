namespace Vitara.Application.Interfaces;

// One food entry as the source reports it.
//
// Nullable throughout because MyFitnessPal's own data is: entries logged against
// custom foods often carry calories and nothing else. A missing macro is stored as
// missing rather than as zero -- a day recorded as "0g protein" reads as a fact, and
// this is health data someone may act on.
public record DiaryEntry(
    string Meal,
    string Name,
    double? Quantity,
    string? Unit,
    double? Calories,
    double? Protein,
    double? Carbs,
    double? Fat,
    double? Fiber);

public record DiaryTotals(
    double? Calories, double? Protein, double? Carbs,
    double? Fat, double? Fiber, double? Sugar, double? Sodium);

public record DiaryGoals(double? Calories, double? Protein, double? Carbs, double? Fat);

public record DiaryDay(string Date, int EntryCount, List<DiaryEntry> Entries, DiaryTotals Totals, DiaryGoals Goals);

// A food diary Vitara can read on a schedule.
//
// An interface rather than a direct HTTP call so the worker is testable without a
// running Python sidecar, and so swapping MyFitnessPal for Cronometer later is one
// implementation rather than a rewrite of the sync.
public interface INutritionSource
{
    // Whether the source is configured at all. False means the worker does nothing and
    // says nothing, rather than failing loudly once a day about a feature not in use.
    bool IsConfigured { get; }

    // Null means the day could not be fetched. It never means "nothing was eaten" --
    // an empty diary comes back as a DiaryDay with no entries, and the difference
    // matters: one is a fact about the user, the other is a fact about the network.
    Task<DiaryDay?> GetDiaryAsync(DateOnly day, CancellationToken ct = default);
}
