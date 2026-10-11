using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NorthStar.Domain.Entities;
using NorthStar.Infrastructure.Data;

namespace NorthStar.Tests;

// The rules memory writes follow: no duplicates, nothing overwritten, an unconfirmed claim
// never displaces a confirmed one, and only live memories are recalled.
public class MemoryLifecycleTests
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

    private static MemoryEntry Draft(string content, string status, Guid? supersedes = null, string kind = "preference") =>
        new() { Content = content, Kind = kind, Status = status, Source = "test", Supersedes = supersedes };

    // San's distiller asks the model for exactly these kinds. "fact" was missing from
    // NorthStar's list, so every fact it extracted was refused and lost.
    [Fact]
    public void EveryKindTheDistillerWritesIsAccepted()
    {
        foreach (var kind in new[] { "preference", "fact", "decision", "event", "observation" })
            Assert.True(MemoryKinds.IsValid(kind), kind);
    }

    [Fact]
    public async Task TheSameMemoryTwiceIsStoredOnce()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        var first = await repo.RememberAsync(Draft("Prefers window seats on flights", MemoryStatus.Proposed));
        var again = await repo.RememberAsync(Draft("prefers window seats on flights.", MemoryStatus.Proposed));

        Assert.False(first.Duplicate);
        Assert.True(again.Duplicate);
        Assert.Equal(first.Memory!.Id, again.Memory!.Id);
        Assert.Single(await repo.GetRecentMemoriesAsync(10, null, "all"));
    }

    [Fact]
    public async Task RestatingAProposedMemoryWithAuthorityConfirmsIt()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        var proposed = await repo.RememberAsync(Draft("Allergic to penicillin", MemoryStatus.Proposed, kind: "fact"));
        var stated = await repo.RememberAsync(Draft("Allergic to penicillin", MemoryStatus.Active, kind: "fact"));

        Assert.True(stated.Duplicate);
        Assert.Equal(MemoryStatus.Active, (await repo.GetMemoryDetailAsync(proposed.Memory!.Id))!.Memory.Status);
    }

    [Fact]
    public async Task AnActiveCorrectionSupersedesAtOnceAndOnlyTheNewOneIsRecalled()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        var old = await repo.RememberAsync(Draft("Coffee order is a flat white", MemoryStatus.Active));
        await repo.RememberAsync(Draft("Coffee order is an oat latte", MemoryStatus.Active, old.Memory!.Id));

        var found = await repo.RecallMemoriesAsync("coffee order", null, 8);

        Assert.Single(found);
        Assert.Contains("oat latte", found[0].Content);
        var before = (await repo.GetMemoryDetailAsync(old.Memory.Id))!.Memory;
        Assert.Equal(MemoryStatus.Superseded, before.Status);
        Assert.Equal(found[0].Id, before.SupersededBy);
    }

    // The core rule: an agent cannot overwrite what the owner confirmed by asserting
    // something else. Its correction waits for confirmation.
    [Fact]
    public async Task AProposedCorrectionNeverDisplacesAConfirmedMemoryUntilConfirmed()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        var confirmed = await repo.RememberAsync(Draft("Lives in Hyderabad", MemoryStatus.Active, kind: "fact"));
        var correction = await repo.RememberAsync(Draft("Lives in Bengaluru", MemoryStatus.Proposed, confirmed.Memory!.Id, "fact"));

        Assert.NotNull(correction.Note);
        Assert.Equal(MemoryStatus.Active, (await repo.GetMemoryDetailAsync(confirmed.Memory.Id))!.Memory.Status);

        var (after, error) = await repo.ConfirmMemoryAsync(correction.Memory!.Id);

        Assert.Null(error);
        Assert.Equal(MemoryStatus.Active, after!.Status);
        Assert.Equal(MemoryStatus.Superseded, (await repo.GetMemoryDetailAsync(confirmed.Memory.Id))!.Memory.Status);
    }

    [Fact]
    public async Task RetractingACorrectionRestoresWhatItReplacedForReview()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        var old = await repo.RememberAsync(Draft("Gym on Mondays", MemoryStatus.Active));
        var wrong = await repo.RememberAsync(Draft("Gym on Tuesdays", MemoryStatus.Active, old.Memory!.Id));

        await repo.RetractMemoryAsync(wrong.Memory!.Id, "wrong day");

        var restored = (await repo.GetMemoryDetailAsync(old.Memory.Id))!.Memory;
        Assert.Equal(MemoryStatus.Proposed, restored.Status);
        Assert.Null(restored.SupersededBy);
        var found = await repo.RecallMemoriesAsync("gym", null, 8);
        Assert.Single(found);
        Assert.Equal(old.Memory.Id, found[0].Id);
    }

    [Fact]
    public async Task RetractedMemoriesAreNotRecalled()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        var m = await repo.RememberAsync(Draft("Meeting with the ECSE team at 2 PM", MemoryStatus.Proposed, kind: "event"));
        await repo.RetractMemoryAsync(m.Memory!.Id, "stale");

        Assert.Empty(await repo.RecallMemoriesAsync("ECSE meeting", null, 8));
    }

    [Fact]
    public async Task AConfirmedMemoryOutranksAnUnconfirmedOneThatMatchesAsWell()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        await repo.RememberAsync(Draft("Favourite tea is masala chai", MemoryStatus.Proposed));
        await repo.RememberAsync(Draft("Favourite tea is green tea", MemoryStatus.Active));

        var found = await repo.RecallMemoriesAsync("favourite tea", null, 8);

        Assert.Equal(2, found.Count);
        Assert.Equal(MemoryStatus.Active, found[0].Status);
    }

    [Fact]
    public async Task HistoryRunsOldestToNewest()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        var a = await repo.RememberAsync(Draft("Car is a Corolla", MemoryStatus.Active, kind: "fact"));
        var b = await repo.RememberAsync(Draft("Car is a Civic", MemoryStatus.Active, a.Memory!.Id, "fact"));
        var c = await repo.RememberAsync(Draft("Car is a Civic hybrid", MemoryStatus.Active, b.Memory!.Id, "fact"));

        var history = (await repo.GetMemoryDetailAsync(b.Memory.Id))!.History.Select(m => m.Id).ToList();

        Assert.Equal([a.Memory.Id, b.Memory.Id, c.Memory!.Id], history);
    }

    [Fact]
    public async Task SavingReportsLiveMemoriesThatReadClose()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        await repo.RememberAsync(Draft("Prefers morning workouts before work", MemoryStatus.Active));

        var r = await repo.RememberAsync(Draft("Prefers evening workouts after work", MemoryStatus.Proposed));

        Assert.Contains(r.Similar, m => m.Content.Contains("morning"));
    }

    [Fact]
    public async Task ProvenanceIsKeptWithTheMemory()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        var draft = Draft("Decided to refinance the house in 2027", MemoryStatus.Proposed, kind: "decision");
        draft.SourceRef = "san:chat:a..b";
        draft.Evidence = "User: let's refinance next year, 2027";
        var saved = await repo.RememberAsync(draft);

        var m = (await repo.GetMemoryDetailAsync(saved.Memory!.Id))!.Memory;
        Assert.Equal("san:chat:a..b", m.SourceRef);
        Assert.Contains("refinance", m.Evidence);
    }

    [Fact]
    public async Task ConfirmingSomethingRetractedIsRefused()
    {
        var (conn, repo) = await NewAsync();
        using var _ = conn;
        var m = await repo.RememberAsync(Draft("Likes jazz", MemoryStatus.Proposed));
        await repo.RetractMemoryAsync(m.Memory!.Id, null);

        var (_, error) = await repo.ConfirmMemoryAsync(m.Memory.Id);

        Assert.NotNull(error);
    }
}
