using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;
using NorthStar.Infrastructure.Data;

namespace NorthStar.Tests;

// Meaning-search against a stand-in embedder: each "concept" is one dimension, and the
// words listed for it all land there. Real models do this with hundreds of dimensions;
// what is under test is what recall does with the vectors, not the model.
public class SemanticRecallTests
{
    private sealed class ConceptEmbedder(bool enabled = true, bool broken = false) : IEmbedder
    {
        private static readonly string[][] Concepts =
        [
            ["car", "honda", "civic", "vehicle", "drive"],
            ["coffee", "latte", "espresso"],
            ["doctor", "appointment", "clinic"],
            ["service", "servicing", "maintenance"],
        ];

        public bool Enabled => enabled;
        public string Model => "concepts-v1";
        public int Calls { get; private set; }

        public Task<float[][]?> EmbedAsync(IReadOnlyList<string> texts, bool isQuery, CancellationToken ct = default)
        {
            Calls++;
            if (broken) return Task.FromResult<float[][]?>(null);
            return Task.FromResult<float[][]?>(texts.Select(Vector).ToArray());
        }

        private static float[] Vector(string text)
        {
            var words = MemorySearch.Normalise(text).Split(' ');
            var v = new float[Concepts.Length + 1];
            for (var i = 0; i < Concepts.Length; i++)
                v[i] = words.Count(w => Concepts[i].Contains(w));
            v[^1] = 0.05f; // a little shared noise, as real vectors have
            return v;
        }
    }

    private static async Task<(SqliteConnection Conn, NorthStarRepository Repo)> NewAsync(IEmbedder? embedder)
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var db = new NorthStarDbContext(new DbContextOptionsBuilder<NorthStarDbContext>().UseSqlite(conn).Options);
        await db.Database.EnsureCreatedAsync();
        await MemorySearch.EnsureSchemaAsync(conn);
        return (conn, new NorthStarRepository(db, embedder));
    }

    private static Task Save(NorthStarRepository repo, string content) =>
        repo.RememberAsync(new MemoryEntry { Content = content, Kind = "fact", Status = MemoryStatus.Active });

    // The point of it: no alias, no shared word, still found.
    [Fact]
    public async Task FindsAMemoryThatSharesNoWordWithTheQuestion()
    {
        var (conn, repo) = await NewAsync(new ConceptEmbedder());
        using var _ = conn;
        await Save(repo, "Bought a blue Honda Civic in March 2026");
        await Save(repo, "Favourite drink is an oat latte");
        await repo.EmbedPendingAsync();

        var found = await repo.RecallMemoriesAsync("where is my car parked", null, 8);

        Assert.Equal("Bought a blue Honda Civic in March 2026", Assert.Single(found).Content);
    }

    [Fact]
    public async Task WithoutAModelRecallIsWhatItWasBefore()
    {
        var (conn, repo) = await NewAsync(null);
        using var _ = conn;
        await Save(repo, "Bought a blue Honda Civic in March 2026");

        Assert.Equal(0, await repo.EmbedPendingAsync());
        Assert.Empty(await repo.RecallMemoriesAsync("where is my car parked", null, 8));
    }

    // A stopped model server costs recall its meaning-search, never the recall itself.
    [Fact]
    public async Task ABrokenModelFallsBackToKeywords()
    {
        var (conn, repo) = await NewAsync(new ConceptEmbedder(broken: true));
        using var _ = conn;
        await Save(repo, "Bought a blue Honda Civic in March 2026");

        Assert.Single(await repo.RecallMemoriesAsync("honda", null, 8));
    }

    [Fact]
    public async Task OnlyLiveMemoriesAreEmbeddedAndRecalled()
    {
        var (conn, repo) = await NewAsync(new ConceptEmbedder());
        using var _ = conn;
        var old = await repo.RememberAsync(new MemoryEntry { Content = "Drives a Honda Civic", Kind = "fact", Status = MemoryStatus.Active });
        await repo.RememberAsync(new MemoryEntry
        {
            Content = "Drives a Toyota vehicle now", Kind = "fact", Status = MemoryStatus.Active, Supersedes = old.Memory!.Id,
        });
        await repo.EmbedPendingAsync();

        var found = await repo.RecallMemoriesAsync("my car", null, 8);

        Assert.Equal("Drives a Toyota vehicle now", Assert.Single(found).Content);
    }

    // A memory whose text changed (or a new model) is re-embedded, not matched against
    // a stale vector.
    [Fact]
    public async Task EmbeddingIsDoneOnceUntilTheTextChanges()
    {
        var embedder = new ConceptEmbedder();
        var (conn, repo) = await NewAsync(embedder);
        using var _ = conn;
        await Save(repo, "Car service is due in November");

        Assert.Equal(1, await repo.EmbedPendingAsync());
        Assert.Equal(0, await repo.EmbedPendingAsync());
    }

    [Fact]
    public async Task SimilarityBelowTheFloorIsNotAMatch()
    {
        Assert.Equal(0, MemorySearch.SemanticRelevance(0.4, 0.6));
        Assert.Equal(0.5, MemorySearch.SemanticRelevance(0.6, 0.6), 3);
        Assert.Equal(0.9, MemorySearch.SemanticRelevance(1.0, 0.6), 3);
        await Task.CompletedTask;
    }
}
