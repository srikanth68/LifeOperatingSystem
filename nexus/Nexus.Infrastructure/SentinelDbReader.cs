using System.Globalization;
using Microsoft.Data.Sqlite;
using Nexus.Application.Dtos;
using Nexus.Application.Interfaces;

namespace Nexus.Infrastructure;

// Read-only SQLite reader for sentinel.db. Sentinel (Python, built separately)
// owns the schema and all writes — this class only ever opens the file in
// Mode=ReadOnly, which is safe to do concurrently with Sentinel's WAL writer.
//
// Column names below are inferred from API_CONTRACT.md's JSON field names
// (camelCase -> snake_case) since INTEGRATION.md (the authoritative schema
// doc) wasn't available when this was written. If the real sentinel.db uses
// different column names, this is the only file that needs updating — DTOs
// and controllers are unaffected.
public class SentinelDbReader : ISentinelReader
{
    private readonly string _dbPath;

    public SentinelDbReader(string dbPath)
    {
        _dbPath = dbPath;
    }

    public bool DatabaseAvailable => File.Exists(_dbPath);

    private SqliteConnection OpenReadOnly()
    {
        if (!DatabaseAvailable)
            throw new SentinelUnavailableException(
                "Sentinel database not found — the engine hasn't started yet.");

        var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly;Cache=Shared");
        conn.Open();
        return conn;
    }

    public async Task<List<BoardRow>> GetBoardAsync()
    {
        using var conn = OpenReadOnly();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT symbol, price, change_pct, action, conviction, composite, edge,
                   risk_approved, risk_entry, risk_stop, risk_target, risk_rr,
                   recommended_style, swing_side, swing_entry_low, swing_entry_high,
                   day_or_state, day_bias, day_vwap, day_rvol, freshness, ran_at
            FROM v_board
            ORDER BY conviction DESC";

        var rows = new List<BoardRow>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new BoardRow
            {
                Symbol = reader.GetString(0),
                Price = reader.GetDouble(1),
                ChangePct = reader.IsDBNull(2) ? null : reader.GetDouble(2),
                Action = reader.GetString(3),
                Conviction = reader.GetInt32(4),
                Composite = reader.GetDouble(5),
                Edge = reader.GetDouble(6),
                RiskApproved = reader.GetBoolean(7),
                RiskEntry = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                RiskStop = reader.IsDBNull(9) ? null : reader.GetDouble(9),
                RiskTarget = reader.IsDBNull(10) ? null : reader.GetDouble(10),
                RiskRr = reader.IsDBNull(11) ? null : reader.GetDouble(11),
                RecommendedStyle = reader.IsDBNull(12) ? null : reader.GetString(12),
                SwingSide = reader.IsDBNull(13) ? null : reader.GetString(13),
                SwingEntryLow = reader.IsDBNull(14) ? null : reader.GetDouble(14),
                SwingEntryHigh = reader.IsDBNull(15) ? null : reader.GetDouble(15),
                DayOrState = reader.IsDBNull(16) ? null : reader.GetString(16),
                DayBias = reader.IsDBNull(17) ? null : reader.GetString(17),
                DayVwap = reader.IsDBNull(18) ? null : reader.GetDouble(18),
                DayRvol = reader.IsDBNull(19) ? null : reader.GetDouble(19),
                Freshness = reader.GetString(20),
                RanAt = ParseUtc(reader.GetString(21)),
            });
        }
        return rows;
    }

    public async Task<string?> GetTickerDetailJsonAsync(string symbol)
    {
        using var conn = OpenReadOnly();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT detail_json FROM v_board WHERE symbol = @symbol";
        cmd.Parameters.AddWithValue("@symbol", symbol.ToUpperInvariant());
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    public async Task<List<HistoryPoint>> GetHistoryAsync(string symbol, int limit)
    {
        using var conn = OpenReadOnly();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ran_at, price, action, conviction, composite, freshness
            FROM runs
            WHERE symbol = @symbol
            ORDER BY ran_at ASC
            LIMIT @limit";
        cmd.Parameters.AddWithValue("@symbol", symbol.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@limit", limit);

        var rows = new List<HistoryPoint>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new HistoryPoint
            {
                RanAt = ParseUtc(reader.GetString(0)),
                Price = reader.GetDouble(1),
                Action = reader.GetString(2),
                Conviction = reader.GetInt32(3),
                Composite = reader.GetDouble(4),
                Freshness = reader.GetString(5),
            });
        }
        return rows;
    }

    public async Task<List<AlertItem>> GetAlertsAsync(DateTime? since, int limit)
    {
        using var conn = OpenReadOnly();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = since.HasValue
            ? @"SELECT id, symbol, ts, kind, message FROM alerts
                WHERE ts >= @since ORDER BY ts DESC LIMIT @limit"
            : @"SELECT id, symbol, ts, kind, message FROM alerts
                ORDER BY ts DESC LIMIT @limit";
        if (since.HasValue) cmd.Parameters.AddWithValue("@since", since.Value.ToString("O"));
        cmd.Parameters.AddWithValue("@limit", limit);

        var rows = new List<AlertItem>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new AlertItem
            {
                Id = reader.GetInt64(0),
                Symbol = reader.GetString(1),
                Ts = ParseUtc(reader.GetString(2)),
                Kind = reader.GetString(3),
                Message = reader.GetString(4),
            });
        }
        return rows;
    }

    public async Task<List<PositionRow>> GetPositionsAsync()
    {
        using var conn = OpenReadOnly();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT symbol, quantity, avg_cost, current_price, market_value,
                   unrealized_pl, unrealized_pl_pct, updated_at
            FROM positions
            ORDER BY market_value DESC";

        var rows = new List<PositionRow>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new PositionRow
            {
                Symbol = reader.GetString(0),
                Quantity = reader.GetDouble(1),
                AvgCost = reader.GetDouble(2),
                CurrentPrice = reader.GetDouble(3),
                MarketValue = reader.GetDouble(4),
                UnrealizedPl = reader.GetDouble(5),
                UnrealizedPlPct = reader.GetDouble(6),
                UpdatedAt = ParseUtc(reader.GetString(7)),
            });
        }
        return rows;
    }

    public async Task<List<WatchItem>> GetWatchlistAsync()
    {
        using var conn = OpenReadOnly();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT symbol, origin, note, added_at FROM watchlist ORDER BY added_at DESC";

        var rows = new List<WatchItem>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new WatchItem
            {
                Symbol = reader.GetString(0),
                Origin = reader.GetString(1),
                Note = reader.IsDBNull(2) ? null : reader.GetString(2),
                AddedAt = ParseUtc(reader.GetString(3)),
            });
        }
        return rows;
    }

    public async Task<StatusDto> GetStatusAsync()
    {
        using var conn = OpenReadOnly();

        int schemaVersion = 1;
        try
        {
            using var svCmd = conn.CreateCommand();
            svCmd.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
            var sv = await svCmd.ExecuteScalarAsync();
            if (sv != null) schemaVersion = Convert.ToInt32(sv, CultureInfo.InvariantCulture);
        }
        catch (SqliteException) { /* meta table shape may differ from this guess — default stands */ }

        DateTime? lastRunAt = null;
        using (var lrCmd = conn.CreateCommand())
        {
            lrCmd.CommandText = "SELECT MAX(ran_at) FROM runs";
            var lr = await lrCmd.ExecuteScalarAsync();
            if (lr is string s) lastRunAt = ParseUtc(s);
        }

        int trackedCount;
        using (var tcCmd = conn.CreateCommand())
        {
            tcCmd.CommandText = "SELECT COUNT(*) FROM watchlist";
            trackedCount = Convert.ToInt32(await tcCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        int openAlerts24h;
        using (var oaCmd = conn.CreateCommand())
        {
            oaCmd.CommandText = "SELECT COUNT(*) FROM alerts WHERE ts >= @since";
            oaCmd.Parameters.AddWithValue("@since", DateTime.UtcNow.AddHours(-24).ToString("O"));
            openAlerts24h = Convert.ToInt32(await oaCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        return new StatusDto
        {
            SchemaVersion = schemaVersion,
            LastRunAt = lastRunAt,
            MarketOpen = IsUsMarketOpen(DateTime.UtcNow),
            TrackedCount = trackedCount,
            OpenAlerts24h = openAlerts24h,
        };
    }

    // US equity regular trading hours: Mon-Fri, 9:30-16:00 America/New_York.
    private static bool IsUsMarketOpen(DateTime utcNow)
    {
        TimeZoneInfo et;
        try { et = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch (TimeZoneNotFoundException) { et = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }

        var nowEt = TimeZoneInfo.ConvertTimeFromUtc(utcNow, et);
        if (nowEt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;

        var open = nowEt.Date.AddHours(9).AddMinutes(30);
        var close = nowEt.Date.AddHours(16);
        return nowEt >= open && nowEt < close;
    }

    private static DateTime ParseUtc(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
