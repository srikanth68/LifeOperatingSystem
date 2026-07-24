using Nexus.Application.Dtos;

namespace Nexus.Application.Interfaces;

public interface ISentinelReader
{
    bool DatabaseAvailable { get; }
    Task<List<BoardRow>> GetBoardAsync();
    Task<string?> GetTickerDetailJsonAsync(string symbol);
    Task<List<HistoryPoint>> GetHistoryAsync(string symbol, int limit);
    Task<List<AlertItem>> GetAlertsAsync(DateTime? since, int limit);
    Task<List<PositionRow>> GetPositionsAsync();
    Task<List<WatchItem>> GetWatchlistAsync();
    Task<StatusDto> GetStatusAsync();
}

// Thrown when sentinel.db doesn't exist yet — maps to HTTP 503 per API_CONTRACT.md §7.
public class SentinelUnavailableException : Exception
{
    public SentinelUnavailableException(string message) : base(message) { }
}
