using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using San.Application;
using San.Application.Interfaces;

namespace San.Worker;

// San updating its own brain. Periodically looks at the chat that's happened
// since the last pass and, if there's anything worth remembering long-term,
// asks the LLM to distill it into durable memories saved into NorthStar (San's
// brain). Runs out-of-band so it never slows down the live chat, and is fully
// best-effort — a failed pass just retries next interval.
public class MemoryDistillationWorker(IServiceProvider services, ILogger<MemoryDistillationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private const string LastDistilledKey = "memory.last_distilled_utc";

    // Kept deliberately small — the gemma-4-E4B model returns empty/garbage on
    // large prompts, so the extraction instruction is short and the output format
    // is trivial to parse.
    private const string ExtractionPrompt =
        "You extract durable long-term memories from a short conversation between a user and their " +
        "assistant. Output ONLY memories worth remembering for months: stable preferences, personal " +
        "facts, decisions made, or notable life events. Ignore small talk, questions, and transient " +
        "status. NEVER record that a reminder, alert, task or calendar event was created - those are " +
        "already stored in the app that owns them, and repeating them here makes the assistant claim " +
        "it set reminders it never set. Write absolute dates (2026-08-17), never today or tomorrow. " +
        "Format: one memory per line as `kind|text`, where kind is one of preference, fact, " +
        "decision, event. If there is nothing worth saving, output exactly: NONE. No other text.";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("San memory distillation worker started. Interval: {m}m", Interval.TotalMinutes);
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch (TaskCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await DistillAsync(ct);
                await RecordAsync(true, null, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Memory distillation pass failed");
                await RecordAsync(false, ex.Message, ct);
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task DistillAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISanRepository>();
        var chat = scope.ServiceProvider.GetRequiredService<IChatProvider>();
        var brain = scope.ServiceProvider.GetRequiredService<IModuleContextService>();

        var lastRaw = await repo.GetSettingAsync(LastDistilledKey);
        DateTime lastUtc = DateTime.TryParse(lastRaw, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed) ? parsed : DateTime.MinValue;

        // Fetches well beyond one interval's worth. At 50 a burst of more than fifty
        // messages inside fifteen minutes pushed the oldest out of the window before
        // the timestamp filter ever saw them, and the cursor then skipped past them.
        var history = await repo.GetChatHistoryAsync(300);
        var since = history.Where(m => m.CreatedAt > lastUtc).ToList();
        // Need at least a full exchange to have anything worth distilling.
        if (since.Count < 2) return;

        var transcript = string.Join("\n", since.Select(m => $"{(m.Role == "user" ? "User" : "San")}: {m.Content}"));
        var reply = await chat.CompleteAsync(ExtractionPrompt, [new ChatTurn("user", transcript)], ct);

        var newestUtc = since.Max(m => m.CreatedAt);

        if (string.IsNullOrWhiteSpace(reply) || reply.Contains("NONE", StringComparison.OrdinalIgnoreCase))
        {
            // Nothing worth keeping is a real answer — move on so this window is not
            // re-examined forever.
            await repo.SetSettingAsync(LastDistilledKey, newestUtc.ToString("O"));
            return;
        }

        int saved = 0;
        int failed = 0;
        int rejected = 0;
        foreach (var line in reply.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sep = line.IndexOf('|');
            if (sep <= 0 || sep >= line.Length - 1) continue;
            var kind = line[..sep].Trim().ToLowerInvariant();
            var text = line[(sep + 1)..].Trim();
            if (text.Length < 3) continue;
            if (kind is not ("preference" or "fact" or "decision" or "event")) kind = "observation";

            // The prompt above asks for this too, but asking is not enough: every one of
            // the reminder-echo memories now polluting recall was produced by a model
            // that had been told to ignore transient status.
            if (!MemoryWorthKeeping.Keep(text, out var why))
            {
                logger.LogInformation("Memory rejected ({Why}): {Text}", why, text);
                rejected++;
                continue;
            }

            var importance = kind is "preference" or "decision" ? 4 : 3;
            if (await brain.SaveMemoryAsync(text, kind, importance, ct)) saved++;
            else failed++;
        }

        // The cursor used to advance immediately after the model replied, before any
        // of this ran — and SaveMemoryAsync swallowed its failures. NorthStar being
        // down for a single fifteen-minute window therefore lost that window's
        // memories permanently and silently: the writes failed, the cursor had already
        // moved past them, and nothing ever looked at those messages again.
        //
        // Hold the cursor only when EVERYTHING failed, which is what a NorthStar
        // outage looks like and costs nothing to retry since none of it was stored.
        // A partial failure still advances: re-running would duplicate the memories
        // that did save, and one lost memory beats a growing pile of repeats.
        if (failed > 0 && saved == 0)
        {
            logger.LogWarning(
                "Distillation held back: all {Failed} memory write(s) failed — leaving the cursor so this window retries.",
                failed);
            return;
        }

        await repo.SetSettingAsync(LastDistilledKey, newestUtc.ToString("O"));

        if (failed > 0)
            logger.LogWarning("Distilled {Saved} memory(ies); {Failed} could not be saved and are lost.", saved, failed);
        else if (saved > 0)
            logger.LogInformation("Distilled {Saved} memory(ies) into NorthStar from recent chat; "
                + "{Rejected} rejected as not durable.", saved, rejected);
    }

    // A pass that found nothing to distill still counts as a completed pass — this
    // marks the timer alive, not the output useful.
    private async Task RecordAsync(bool ok, string? error, CancellationToken ct)
    {
        try
        {
            using var scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IHealthTracker>()
                .RecordAsync(HealthComponents.WorkerMemoryDistillation, ok, error, ct);
        }
        catch { /* bookkeeping must never be why a pass is considered failed */ }
    }
}
