namespace Nexus.Application.Dtos;

// Mirrors API_CONTRACT.md §2 — GET /board. One row per tracked symbol.
public record BoardRow
{
    public required string Symbol { get; init; }
    public required double Price { get; init; }
    public double? ChangePct { get; init; }
    public required string Action { get; init; }
    public required int Conviction { get; init; }
    public required double Composite { get; init; }
    public required double Edge { get; init; }
    public required bool RiskApproved { get; init; }
    public double? RiskEntry { get; init; }
    public double? RiskStop { get; init; }
    public double? RiskTarget { get; init; }
    public double? RiskRr { get; init; }
    public string? RecommendedStyle { get; init; }
    public string? SwingSide { get; init; }
    public double? SwingEntryLow { get; init; }
    public double? SwingEntryHigh { get; init; }
    public string? DayOrState { get; init; }
    public string? DayBias { get; init; }
    public double? DayVwap { get; init; }
    public double? DayRvol { get; init; }
    public required string Freshness { get; init; }
    public required DateTime RanAt { get; init; }
}

// Mirrors API_CONTRACT.md §4 — GET /tickers/{symbol}/history
public record HistoryPoint
{
    public required DateTime RanAt { get; init; }
    public required double Price { get; init; }
    public required string Action { get; init; }
    public required int Conviction { get; init; }
    public required double Composite { get; init; }
    public required string Freshness { get; init; }
}

// Mirrors API_CONTRACT.md §4 — GET /alerts
public record AlertItem
{
    public required long Id { get; init; }
    public required string Symbol { get; init; }
    public required DateTime Ts { get; init; }
    public required string Kind { get; init; }
    public required string Message { get; init; }
}

// Mirrors API_CONTRACT.md §4 — GET /positions
public record PositionRow
{
    public required string Symbol { get; init; }
    public required double Quantity { get; init; }
    public required double AvgCost { get; init; }
    public required double CurrentPrice { get; init; }
    public required double MarketValue { get; init; }
    public required double UnrealizedPl { get; init; }
    public required double UnrealizedPlPct { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

// Mirrors API_CONTRACT.md §4 — GET /watchlist
public record WatchItem
{
    public required string Symbol { get; init; }
    public required string Origin { get; init; }
    public string? Note { get; init; }
    public required DateTime AddedAt { get; init; }
}

// Mirrors API_CONTRACT.md §4 — GET /status
public record StatusDto
{
    public required int SchemaVersion { get; init; }
    public DateTime? LastRunAt { get; init; }
    public required bool MarketOpen { get; init; }
    public required int TrackedCount { get; init; }
    public required int OpenAlerts24h { get; init; }
}

// Mirrors API_CONTRACT.md §7 — error body shape
public record ErrorResponse
{
    public required string Error { get; init; }
    public required string Message { get; init; }
}
