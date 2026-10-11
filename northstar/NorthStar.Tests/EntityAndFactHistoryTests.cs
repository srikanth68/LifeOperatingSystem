using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NorthStar.Domain.Entities;
using NorthStar.Infrastructure.Data;

namespace NorthStar.Tests;

public class EntityAndFactHistoryTests
{
    private static async Task<(SqliteConnection Conn, NorthStarRepository Repo)> NewAsync()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var db = new NorthStarDbContext(new DbContextOptionsBuilder<NorthStarDbContext>().UseSqlite(conn).Options);
        await db.Database.EnsureCreatedAsync();
        await MemorySearch.EnsureSchemaAsync(conn);
        return (conn, new NorthStarRepository(db));
    }

    // ── Entities in recall ──

    // What keyword search cannot do: the memory never says "car", the question never
    // says "Honda". The alias is the bridge.
    [Fact]
    public async Task AnAliasFindsMemoriesThatNeverUseTheWord()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        await repo.RememberAsync(new MemoryEntry { Content = "Bought a blue Honda Civic in March 2026", Kind = "event" },
            ["vehicle:Honda Civic"]);
        var civic = await repo.ResolveEntityAsync("Honda Civic");
        await repo.UpdateEntityAsync(civic!.Id, null, null, "my car", null);

        var found = await repo.RecallMemoriesAsync("when is my car due for service", null, 8);

        Assert.Single(found);
        Assert.Contains("Honda", found[0].Content);
    }

    [Fact]
    public async Task ACorrectionStaysReachableThroughTheEntityOfWhatItCorrected()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        var old = await repo.RememberAsync(new MemoryEntry
            { Content = "Bought a blue Honda Civic in March 2026", Kind = "event", Status = MemoryStatus.Active },
            ["vehicle:Honda Civic"]);
        var civic = await repo.ResolveEntityAsync("Honda Civic");
        await repo.UpdateEntityAsync(civic!.Id, null, null, "my car", null);

        await repo.RememberAsync(new MemoryEntry
        {
            Content = "Bought a grey one in March 2026, not blue", Kind = "event",
            Status = MemoryStatus.Active, Supersedes = old.Memory!.Id,
        });

        var found = Assert.Single(await repo.RecallMemoriesAsync("when is my car due for service", null, 8));
        Assert.Contains("grey", found.Content);
    }

    [Fact]
    public async Task SavingWithTheSameEntityTwiceMakesOneEntity()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        await repo.RememberAsync(new MemoryEntry { Content = "Rent for the Hyderabad flat is due on the 5th" }, ["property:Hyderabad flat"]);
        await repo.RememberAsync(new MemoryEntry { Content = "Hyderabad flat needs new tiles" }, ["hyderabad FLAT"]);

        var all = await repo.FindEntitiesAsync(null);
        Assert.Single(all);
        Assert.Equal("property", all[0].Type);
        var detail = await repo.GetEntityDetailAsync(all[0].Id);
        Assert.Equal(2, detail!.Links.Count);
        Assert.All(detail.Links, l => Assert.Equal(RecordTypes.Memory, l.OtherType));
    }

    [Fact]
    public async Task AnEntityNamedByAStopWordIsNeverMatched()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        await repo.UpsertEntityAsync("It");

        Assert.Empty(await repo.EntitiesNamedInAsync("is it raining"));
    }

    [Fact]
    public async Task ALinkToARecordThatDoesNotExistIsRefused()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        var (e, _, _) = await repo.UpsertEntityAsync("Project Maaya", "project");

        var (link, _, error) = await repo.AddLinkAsync(new RecordLink
        {
            FromType = RecordTypes.Memory, FromId = Guid.NewGuid().ToString(), Relation = "about",
            ToType = RecordTypes.Entity, ToId = e!.Id.ToString(),
        });

        Assert.Null(link);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task AnInferredLinkSeenDirectlyBecomesObservedAndIsNotDuplicated()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        var (project, _, _) = await repo.UpsertEntityAsync("Vitara", "project");
        var m = await repo.RememberAsync(new MemoryEntry { Content = "Decided Vitara goes public", Kind = "decision" });
        RecordLink Link(string basis) => new()
        {
            FromType = RecordTypes.Memory, FromId = m.Memory!.Id.ToString().ToUpperInvariant(), Relation = "affects",
            ToType = RecordTypes.Entity, ToId = project!.Id.ToString(), Basis = basis,
        };

        var (first, created1, _) = await repo.AddLinkAsync(Link(LinkBasis.Inferred));
        var (second, created2, _) = await repo.AddLinkAsync(Link(LinkBasis.Observed));

        Assert.True(created1);
        Assert.False(created2);
        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(LinkBasis.Observed, second.Basis);
    }

    [Fact]
    public async Task LinksAreVisibleFromBothEndsWithADescriptionOfTheOther()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        await repo.UpsertFactAsync(new UserFact { Key = "home_city", Value = "Hyderabad" });
        var (city, _, _) = await repo.UpsertEntityAsync("Hyderabad", "place");
        await repo.AddLinkAsync(new RecordLink
        {
            FromType = RecordTypes.Fact, FromId = "home_city", Relation = "names",
            ToType = RecordTypes.Entity, ToId = city!.Id.ToString(),
        });

        var fromFact = await repo.GetLinksAsync(RecordTypes.Fact, "home_city");
        var fromCity = await repo.GetLinksAsync(RecordTypes.Entity, city.Id.ToString());

        Assert.Equal("Hyderabad", Assert.Single(fromFact).OtherSummary);
        Assert.Equal("home_city: Hyderabad", Assert.Single(fromCity).OtherSummary);
        Assert.Equal("in", fromCity[0].Direction);
    }

    // ── Fact history ──

    [Fact]
    public async Task ChangingAFactKeepsWhatItWasAndSettingItAgainDoesNot()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        await repo.UpsertFactAsync(new UserFact { Key = "employer", Value = "Acme", Source = "manual" });
        await repo.UpsertFactAsync(new UserFact { Key = "employer", Value = "Acme", Source = "manual" });
        await repo.UpsertFactAsync(new UserFact { Key = "employer", Value = "Globex", Source = "mcp", SourceRef = "san:chat:x" });

        var history = await repo.GetFactHistoryAsync("employer");

        var only = Assert.Single(history);
        Assert.Equal("Acme", only.Value);
        Assert.Equal(FactChange.Replaced, only.Change);
        Assert.Equal("Globex", (await repo.GetFactAsync("employer"))!.Value);
    }

    [Fact]
    public async Task DeletingAFactLeavesItsLastValueInHistory()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        await repo.UpsertFactAsync(new UserFact { Key = "diet", Value = "vegetarian" });
        await repo.DeleteFactAsync("diet");

        Assert.Null(await repo.GetFactAsync("diet"));
        var gone = Assert.Single(await repo.GetFactHistoryAsync("diet"));
        Assert.Equal(FactChange.Deleted, gone.Change);
        Assert.Equal("vegetarian", gone.Value);
    }
}
