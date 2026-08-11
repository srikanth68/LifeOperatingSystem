using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        "status. Format: one memory per line as `kind|text`, where kind is one of preference, fact, " +
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

        var history = await repo.GetChatHistoryAsync(50);
        var since = history.Where(m => m.CreatedAt > lastUtc).ToList();
        // Need at least a full exchange to have anything worth distilling.
        if (since.Count < 2) return;

        var transcript = string.Join("\n", since.Select(m => $"{(m.Role == "user" ? "User" : "San")}: {m.Content}"));
        var reply = await chat.CompleteAsync(ExtractionPrompt, [new ChatTurn("user", transcript)], ct);

        var newestUtc = since.Max(m => m.CreatedAt);
        await repo.SetSettingAsync(LastDistilledKey, newestUtc.ToString("O"));

        if (string.IsNullOrWhiteSpace(reply) || reply.Contains("NONE", StringComparison.OrdinalIgnoreCase))
            return;

        int saved = 0;
        foreach (var line in reply.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sep = line.IndexOf('|');
            if (sep <= 0 || sep >= line.Length - 1) continue;
            var kind = line[..sep].Trim().ToLowerInvariant();
            var text = line[(sep + 1)..].Trim();
            if (text.Length < 3) continue;
            if (kind is not ("preference" or "fact" or "decision" or "event")) kind = "observation";

            var importance = kind is "preference" or "decision" ? 4 : 3;
            await brain.SaveMemoryAsync(text, kind, importance, ct);
            saved++;
        }
        if (saved > 0) logger.LogInformation("Distilled {n} memory(ies) into NorthStar from recent chat.", saved);
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
