using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Application.Dtos;
using Nexus.Application.Interfaces;

namespace Nexus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/nexus/sentinel")]
public class SentinelController : ControllerBase
{
    private readonly ISentinelReader _reader;

    public SentinelController(ISentinelReader reader)
    {
        _reader = reader;
    }

    [HttpGet("board")]
    public async Task<ActionResult<List<BoardRow>>> GetBoard()
    {
        try { return Ok(await _reader.GetBoardAsync()); }
        catch (SentinelUnavailableException ex) { return Unavailable(ex.Message); }
    }

    [HttpGet("tickers/{symbol}")]
    public async Task<IActionResult> GetTickerDetail(string symbol)
    {
        try
        {
            var json = await _reader.GetTickerDetailJsonAsync(symbol);
            if (json is null)
                return NotFound(new ErrorResponse { Error = "not_found", Message = $"No verdict for symbol {symbol.ToUpperInvariant()}." });

            // detail_json is Sentinel's frozen spec-sheet payload — pass it through verbatim.
            return Content(json, "application/json; charset=utf-8");
        }
        catch (SentinelUnavailableException ex) { return Unavailable(ex.Message); }
    }

    [HttpGet("tickers/{symbol}/history")]
    public async Task<ActionResult<List<HistoryPoint>>> GetHistory(string symbol, [FromQuery] int limit = 100)
    {
        if (limit < 1) return BadRequestError("Bad request", "limit must be a positive integer.");
        limit = Math.Min(limit, 500);

        try { return Ok(await _reader.GetHistoryAsync(symbol, limit)); }
        catch (SentinelUnavailableException ex) { return Unavailable(ex.Message); }
    }

    [HttpGet("alerts")]
    public async Task<ActionResult<List<AlertItem>>> GetAlerts([FromQuery] string? since, [FromQuery] int limit = 100)
    {
        DateTime? sinceUtc = null;
        if (!string.IsNullOrWhiteSpace(since))
        {
            if (!DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.AdjustToUniversal
                    | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                return BadRequestError("Bad request", "since must be an ISO-8601 timestamp.");
            sinceUtc = parsed;
        }
        if (limit < 1) return BadRequestError("Bad request", "limit must be a positive integer.");
        limit = Math.Min(limit, 500);

        try { return Ok(await _reader.GetAlertsAsync(sinceUtc, limit)); }
        catch (SentinelUnavailableException ex) { return Unavailable(ex.Message); }
    }

    [HttpGet("positions")]
    public async Task<ActionResult<List<PositionRow>>> GetPositions()
    {
        try { return Ok(await _reader.GetPositionsAsync()); }
        catch (SentinelUnavailableException ex) { return Unavailable(ex.Message); }
    }

    [HttpGet("watchlist")]
    public async Task<ActionResult<List<WatchItem>>> GetWatchlist()
    {
        try { return Ok(await _reader.GetWatchlistAsync()); }
        catch (SentinelUnavailableException ex) { return Unavailable(ex.Message); }
    }

    [HttpGet("status")]
    public async Task<ActionResult<StatusDto>> GetStatus()
    {
        try { return Ok(await _reader.GetStatusAsync()); }
        catch (SentinelUnavailableException ex) { return Unavailable(ex.Message); }
    }

    private ObjectResult Unavailable(string message) =>
        StatusCode(503, new ErrorResponse { Error = "unavailable", Message = message });

    private BadRequestObjectResult BadRequestError(string error, string message) =>
        BadRequest(new ErrorResponse { Error = error, Message = message });
}
