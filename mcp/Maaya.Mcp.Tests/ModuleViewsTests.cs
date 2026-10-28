using Maaya.Mcp.Tools;

namespace Maaya.Mcp.Tests;

// Every view lands on an endpoint that exists, with its arguments in the shape that
// endpoint reads. The paths asserted here are copied from the module controllers, so a
// typo in a route fails a test rather than a conversation.
public class ModuleViewsTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private const string Pid = "0b3a5c1e-1111-2222-3333-444455556666";

    // ── Picking a view ──

    [Theory]
    [InlineData(null, "summary")]
    [InlineData("", "summary")]
    [InlineData("transaction", "transactions")]
    [InlineData("Transactions", "transactions")]
    [InlineData(" accounts ", "accounts")]
    public void NearMissesResolve(string? asked, string expected)
        => Assert.Equal(expected, ModuleViews.Pick(asked, "summary", ModuleViews.VaultViews));

    [Fact]
    public void SpacedAndHyphenatedNamesResolve()
    {
        Assert.Equal("vo2max", ModuleViews.Pick("VO2 max", "today", ModuleViews.VitaraViews));
        Assert.Equal("spo2", ModuleViews.Pick("SpO2", "today", ModuleViews.VitaraViews));
        Assert.Equal("premarket", ModuleViews.Pick("pre-market", "status", ModuleViews.NexusViews));
    }

    [Fact]
    public void AGenuineMissIsNotGuessed()
    {
        Assert.Null(ModuleViews.Pick("balance sheet", "summary", ModuleViews.VaultViews));
        var msg = ModuleViews.UnknownView("vault_read", "balance sheet", ModuleViews.VaultViews);
        Assert.Contains("transactions", msg);   // the choices come back, so the model can retry
    }

    // ── Vault ──

    [Fact]
    public void VaultDefaultIsTheOldSummary()
        => Assert.Equal("/api/summary", Vault("summary").Path);

    [Fact]
    public void TransactionsCarryEveryFilter()
    {
        var (path, error) = ModuleViews.Vault("transactions", "2026-09-01", "2026-09-30", "home depot", "Shopping", "acc_123", null, Today);
        Assert.Null(error);
        Assert.Equal("/api/transactions?limit=50&startDate=2026-09-01&endDate=2026-09-30&q=home%20depot&category=Shopping&accountId=acc_123", path);
    }

    [Fact]
    public void TransactionLimitIsCapped()
        => Assert.StartsWith("/api/transactions?limit=200", ModuleViews.Vault("transactions", null, null, null, null, null, 5000, Today).Path);

    // "What did I spend in March" must not quietly become "everything ever".
    [Theory]
    [InlineData("March")]
    [InlineData("2026/03/01")]
    [InlineData("01-03-2026")]
    public void AGarbledDateFailsLoudly(string from)
    {
        var (path, error) = ModuleViews.Vault("transactions", from, null, null, null, null, null, Today);
        Assert.Null(path);
        Assert.Contains("YYYY-MM-DD", error);
    }

    [Fact]
    public void MonthlyCountsBackToFrom()
    {
        Assert.Equal("/api/summary/monthly-spending?months=8", ModuleViews.Vault("monthly", "2026-03-01", null, null, null, null, null, Today).Path);
        Assert.Equal("/api/summary/monthly-spending?months=12", Vault("monthly").Path);
    }

    [Theory]
    [InlineData("accounts", "/api/accounts")]
    [InlineData("recurring", "/api/recurring")]
    [InlineData("sync", "/api/sync/status/latest")]
    public void OtherVaultViews(string view, string path) => Assert.Equal(path, Vault(view).Path);

    // ── Vitara ──

    [Fact]
    public void VitaraDefaultIsTheOldDashboard()
        => Assert.Equal("/api/dashboard", ModuleViews.Vitara("today", null, null).Path);

    [Fact]
    public void AWeekOfSleepIsNightByNight()
        => Assert.Equal("/api/sleep?days=7", ModuleViews.Vitara("sleep", null, null).Path);

    [Fact]
    public void AMonthOfSleepIsAverages()
        => Assert.Equal("/api/sleep/summary?days=30", ModuleViews.Vitara("sleep", 30, null).Path);

    [Fact]
    public void TheCutoverIsAtTwoWeeks()
    {
        Assert.Equal("/api/readiness?days=14", ModuleViews.Vitara("readiness", 14, null).Path);
        Assert.Equal("/api/readiness/summary?days=15", ModuleViews.Vitara("readiness", 15, null).Path);
    }

    [Theory]
    [InlineData("workouts", "/api/workouts?days=30")]
    [InlineData("weight", "/api/weighins?days=90")]
    [InlineData("labs", "/api/labs?limit=20")]
    [InlineData("measurements", "/api/measurements?days=90")]
    [InlineData("vo2max", "/api/vo2max?days=180")]
    [InlineData("stress", "/api/stress?days=7")]
    [InlineData("spo2", "/api/spo2?days=7")]
    [InlineData("interventions", "/api/interventions")]
    public void OtherVitaraViews(string view, string path)
        => Assert.Equal(path, ModuleViews.Vitara(view, null, null).Path);

    [Fact]
    public void MeasurementsFilterByMetric()
        => Assert.Equal("/api/measurements?days=30&metric=body_fat", ModuleViews.Vitara("measurements", 30, "body_fat").Path);

    [Fact]
    public void DaysAreClamped()
        => Assert.Equal("/api/workouts?days=365", ModuleViews.Vitara("workouts", 9999, null).Path);

    // ── Aasthi ──

    [Fact]
    public void AasthiDefaultIsTheOldPropertyList()
        => Assert.Equal("/api/properties", ModuleViews.Aasthi("properties", null, null, null).Path);

    [Theory]
    [InlineData("property")]
    [InlineData("documents")]
    public void OnePropertyViewsNeedAProperty(string view)
    {
        var (path, error) = ModuleViews.Aasthi(view, null, null, null);
        Assert.Null(path);
        Assert.Contains("address", error);
    }

    [Fact]
    public void OnePropertyViews()
    {
        Assert.Equal($"/api/properties/{Pid}", ModuleViews.Aasthi("property", Pid, null, null).Path);
        Assert.Equal($"/api/properties/{Pid}/documents", ModuleViews.Aasthi("documents", Pid, null, null).Path);
    }

    [Fact]
    public void FinancialsAndMaintenanceRollUpWhenNoPropertyIsNamed()
    {
        Assert.Equal("/api/financials/summary", ModuleViews.Aasthi("financials", null, null, null).Path);
        Assert.Equal($"/api/properties/{Pid}/financials", ModuleViews.Aasthi("financials", Pid, null, null).Path);
        Assert.Equal("/api/maintenance/summary", ModuleViews.Aasthi("maintenance", null, null, null).Path);
        Assert.Equal($"/api/properties/{Pid}/maintenance", ModuleViews.Aasthi("maintenance", Pid, null, null).Path);
    }

    [Fact]
    public void TasksAndRentFilterByProperty()
    {
        Assert.Equal("/api/tasks", ModuleViews.Aasthi("tasks", null, null, null).Path);
        Assert.Equal($"/api/tasks?propertyId={Pid}&status=open", ModuleViews.Aasthi("tasks", Pid, "open", null).Path);
        Assert.Equal("/api/recurring-charges/status", ModuleViews.Aasthi("rent", null, null, null).Path);
        Assert.Equal($"/api/recurring-charges/status?propertyId={Pid}", ModuleViews.Aasthi("rent", Pid, null, null).Path);
    }

    [Fact]
    public void UnreceiptedTakesAYear()
    {
        Assert.Equal("/api/ledger/unreceipted", ModuleViews.Aasthi("unreceipted", null, null, null).Path);
        Assert.Equal("/api/ledger/unreceipted?year=2025", ModuleViews.Aasthi("unreceipted", null, null, 2025).Path);
    }

    // ── Karma ──

    [Fact]
    public void HistoryNeedsAHabit()
    {
        Assert.Contains("habit", ModuleViews.Karma("history", null, null, null).Error);
        Assert.Equal("/api/habits/h1/logs?days=30", ModuleViews.Karma("history", "h1", null, null).Path);
    }

    // No path and no error is the tool's signal to fan out over every active habit.
    [Fact]
    public void StatsWithoutAHabitMeansAllOfThem()
    {
        var (path, error) = ModuleViews.Karma("stats", null, null, null);
        Assert.Null(path);
        Assert.Null(error);
        Assert.Equal("/api/habits/h1/stats?days=90", ModuleViews.Karma("stats", "h1", null, null).Path);
    }

    [Fact]
    public void GoalsAndHabits()
    {
        Assert.Equal("/api/habits?active=true", ModuleViews.Karma("habits", null, null, null).Path);
        Assert.Equal("/api/goals", ModuleViews.Karma("goals", null, null, null).Path);
        Assert.Equal("/api/goals?status=completed", ModuleViews.Karma("goals", null, null, "completed").Path);
    }

    // ── Sutra ──

    [Fact]
    public void SutraViews()
    {
        Assert.Equal("/api/documents/stats", ModuleViews.Sutra("stats", null, null, null).Path);
        Assert.Equal("/api/documents?q=passport", ModuleViews.Sutra("search", "passport", null, null).Path);
        Assert.Equal("/api/documents?q=w2&category=tax", ModuleViews.Sutra("search", "w2", "tax", null).Path);
        Assert.Equal("/api/documents/expiring?days=60", ModuleViews.Sutra("expiring", null, null, null).Path);
    }

    // ── Nexus ──

    [Fact]
    public void AlertsLookBackByDays()
        => Assert.Equal("/api/nexus/sentinel/alerts?since=2026-10-20T12%3A00%3A00Z&limit=50",
                        ModuleViews.Nexus("alerts", null, null, null, Now).Path);

    [Fact]
    public void TickerViewsNeedASymbol()
    {
        Assert.Contains("symbol", ModuleViews.Nexus("ticker", null, null, null, Now).Error);
        Assert.Equal("/api/nexus/sentinel/tickers/AAPL", ModuleViews.Nexus("ticker", "aapl", null, null, Now).Path);
        Assert.Equal("/api/nexus/sentinel/tickers/BRK.B/history?limit=30", ModuleViews.Nexus("history", "BRK.B", null, null, Now).Path);
    }

    // The symbol is part of the path, so anything that is not a ticker never gets there.
    [Theory]
    [InlineData("../positions")]
    [InlineData("AAPL/history")]
    [InlineData("apple inc")]
    public void ANonTickerIsRefused(string symbol)
    {
        var (path, error) = ModuleViews.Nexus("ticker", symbol, null, null, Now);
        Assert.Null(path);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("status", "/api/nexus/sentinel/status")]
    [InlineData("board", "/api/nexus/sentinel/board")]
    [InlineData("positions", "/api/nexus/sentinel/positions")]
    [InlineData("watchlist", "/api/nexus/sentinel/watchlist")]
    [InlineData("premarket", "/api/nexus/sentinel/premarket")]
    public void OtherNexusViews(string view, string path)
        => Assert.Equal(path, ModuleViews.Nexus(view, null, null, null, Now).Path);

    // Every listed view routes somewhere: a view added to a list but not to its switch
    // would otherwise be advertised to the model and then refused.
    [Fact]
    public void EveryAdvertisedViewIsRouted()
    {
        foreach (var v in ModuleViews.VaultViews) Assert.Null(ModuleViews.Vault(v, null, null, null, null, null, null, Today).Error);
        foreach (var v in ModuleViews.VitaraViews) Assert.Null(ModuleViews.Vitara(v, null, null).Error);
        foreach (var v in ModuleViews.AasthiViews) Assert.Null(ModuleViews.Aasthi(v, Pid, null, null).Error);
        foreach (var v in ModuleViews.KarmaViews) Assert.Null(ModuleViews.Karma(v, "h1", null, null).Error);
        foreach (var v in ModuleViews.SutraViews) Assert.Null(ModuleViews.Sutra(v, "x", null, null).Error);
        foreach (var v in ModuleViews.NexusViews) Assert.Null(ModuleViews.Nexus(v, "AAPL", null, null, Now).Error);
    }

    private static (string? Path, string? Error) Vault(string view)
        => ModuleViews.Vault(view, null, null, null, null, null, null, Today);
}
