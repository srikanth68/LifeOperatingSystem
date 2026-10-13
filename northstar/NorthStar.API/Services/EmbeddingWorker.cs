using NorthStar.Application.Interfaces;

namespace NorthStar.API.Services;

// Keeps every live memory's embedding current, so recall can match by meaning. New
// and corrected memories are picked up within a minute; a first run, or a switch of
// model, works through the backlog a batch at a time. Does nothing at all when no
// local embedding model is configured (EMBEDDINGS_URL unset).
public class EmbeddingWorker(IServiceScopeFactory scopeFactory, IEmbedder embedder, ILogger<EmbeddingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!embedder.Enabled)
        {
            logger.LogInformation("Meaning-search off: EMBEDDINGS_URL is not set. Recall uses keywords and entities.");
            return;
        }

        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<INorthStarRepository>();
                int done, total = 0;
                // Drain a backlog in batches within one tick, then rest.
                while ((done = await repo.EmbedPendingAsync(32, stoppingToken)) > 0 && total < 1000)
                    total += done;
                if (total > 0) logger.LogInformation("Embedded {Count} memory(ies) for meaning-search.", total);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Embedding pass failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
