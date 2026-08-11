using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using San.Application;
using San.Application.Interfaces;

namespace San.Worker;

// San looking across the whole system for patterns nobody asked about.
//
// NorthStar has had an Insight entity, a REST surface, a dashboard slot and an
// activeInsights field flowing into San's chat context since it was built — and
// nothing ever generated one. The only writer was a manual POST. This is the missing
// producer.
//
// The division of labour is deliberate and is the whole design:
//   NorthStar /api/rollup  — counts. Computes, never interprets.
//   this worker + Gemma    — interprets. Never computes.
//   InsightGrounding       — refuses anything citing figures the data doesn't support.
//
// Asking the model to do arithmetic over raw rows produces confident fiction; asking
// it to notice what a table of real numbers implies is exactly what it is good at.
public class InsightWorker(IServiceProvider services, ILogger<InsightWorker> logger) : BackgroundService
{
    // Daily. Correlations move on the scale of weeks, so running this every fifteen
    // minutes would burn the model on a table that has barely changed and would push
    // the same observation at the user repeatedly.
    private static readonly TimeSpan Interval = TimeSpan.FromHours(
        double.TryParse(Environment.GetEnvironmentVariable("INSIGHT_INTERVAL_HOURS"), out var h) && h > 0 ? h : 24);

    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(11);

    private const string LastRunKey = "insights.last_run_utc";

    private const string Prompt =
        "You are San, looking at a table of the user's own life data — week by week counts of what " +
        "happened across their finances, health, habits and property, plus total money recorded each " +
        "week.\n\n" +
        "Find things WORTH TELLING THEM that they would not notice themselves. Relationships between " +
        "different areas are the most valuable: spending against sleep, habit consistency against " +
        "activity, property costs against everything else. A trend that has held for several weeks is " +
        "worth more than a single unusual week.\n\n" +
        "RULES:\n" +
        "- Use ONLY numbers that appear in the table. Never estimate, extrapolate or invent a figure. " +
        "An insight citing a number that is not in the data is worse than no insight.\n" +
        "- Say nothing about weeks with almost no data — sparse weeks are missing records, not quiet " +
        "weeks.\n" +
        "- No praise, no filler, no restating a single number back. \"You spent $400 last week\" is not " +
        "an insight; \"your spending is highest in the weeks you log the fewest habits\" is.\n" +
        "- At most 3. Fewer is better. None is a perfectly good answer.\n\n" +
        "Return ONLY a JSON array, no prose and no code fence:\n" +
        "[{\"title\":\"short headline\",\"body\":\"one or two sentences with the supporting numbers\"}]\n\n" +
        "If nothing in the table is genuinely worth surfacing, return exactly: []";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Insight worker started. Interval: {h}h (first run in {m}m)",
            Interval.TotalHours, StartupDelay.TotalMinutes);

        try { await Task.Delay(StartupDelay, ct); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RunAsync(ct);
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        string? error = null;
        try
        {
            using var scope = services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISanRepository>();
            var chat = scope.ServiceProvider.GetRequiredService<IChatProvider>();
            var brain = scope.ServiceProvider.GetRequiredService<IModuleContextService>();

            var rollup = await brain.GetRollupAsync(ct);
            if (string.IsNullOrWhiteSpace(rollup))
            {
                logger.LogInformation("Insights: no rollup available — skipping.");
                return;
            }

            // Too little history and any "pattern" is noise. Better to stay silent for
            // a few weeks than to teach the user that insights are meaningless.
            if (CountWeeks(rollup) < 3)
            {
                logger.LogInformation("Insights: fewer than 3 weeks of data — too early to draw anything.");
                return;
            }

            var reply = await chat.CompleteAsync(
                Prompt + "\n\n" + SanOutputConventions.Text,
                [new ChatTurn("user", rollup)], ct);

            var proposed = ParseInsights(reply);
            if (proposed.Count == 0)
            {
                logger.LogInformation("Insights: nothing worth surfacing this run.");
                await repo.SetSettingAsync(LastRunKey, DateTime.UtcNow.ToString("O"));
                return;
            }

            var accepted = 0;
            foreach (var p in proposed)
            {
                if (!InsightGrounding.IsGrounded(p, rollup, out var why))
                {
                    // Dropped, not corrected: fixing the number would leave the claim
                    // around it intact while quietly changing what it asserts.
                    logger.LogWarning("Insights: REJECTED \"{Title}\" — {Why}.", p.Title, why);
                    continue;
                }

                if (await brain.SaveInsightAsync(p.Title, p.Body, ct)) accepted++;
            }

            logger.LogInformation("Insights: {Accepted} of {Total} proposal(s) accepted into NorthStar.",
                accepted, proposed.Count);
            await repo.SetSettingAsync(LastRunKey, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Insight run failed");
            error = ex.Message;
        }
        finally
        {
            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IHealthTracker>()
                    .RecordAsync(HealthComponents.WorkerInsights, error is null, error, ct);
            }
            catch { /* bookkeeping must never be why a run counts as failed */ }
        }
    }

    private static int CountWeeks(string rollup)
    {
        try
        {
            using var doc = JsonDocument.Parse(rollup);
            return doc.RootElement.TryGetProperty("weeks", out var w) && w.TryGetInt32(out var n) ? n : 0;
        }
        catch (JsonException) { return 0; }
    }

    private static List<ProposedInsight> ParseInsights(string reply)
    {
        var list = new List<ProposedInsight>();
        var trimmed = (reply ?? "").Trim();
        if (trimmed.Length == 0) return list;

        var start = trimmed.IndexOf('[');
        var end = trimmed.LastIndexOf(']');
        if (start < 0 || end <= start) return list;

        try
        {
            using var doc = JsonDocument.Parse(trimmed[start..(end + 1)]);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var title = el.TryGetProperty("title", out var t) ? t.GetString() : null;
                var body = el.TryGetProperty("body", out var b) ? b.GetString() : null;
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body)) continue;
                list.Add(new ProposedInsight(title.Trim(), body.Trim()));
            }
        }
        catch (JsonException)
        {
            // Unlike a finding, an unparseable insight is simply dropped. There is no
            // deadline being missed and no user waiting — a malformed one is worth less
            // than the confusion of delivering prose as though it were analysis.
        }

        return list.Take(3).ToList();
    }
}
