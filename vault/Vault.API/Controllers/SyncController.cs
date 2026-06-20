using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vault.API.Models;
using Vault.Worker.Data;
using Vault.Worker.Services;

namespace Vault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly ISyncService _syncService;
    private readonly VaultDbContext _db;
    private readonly ILogger<SyncController> _logger;

    public SyncController(ISyncService syncService, VaultDbContext db, ILogger<SyncController> logger)
    {
        _syncService = syncService;
        _db = db;
        _logger = logger;
    }

    [HttpPost("trigger")]
    public async Task<ActionResult<SyncStatusDto>> TriggerSync()
    {
        _logger.LogInformation("Manual sync triggered via API");

        var result = await _syncService.SyncAsync();

        return Ok(new SyncStatusDto
        {
            Id = result.Id,
            Status = result.Status,
            LastSyncTime = result.LastSyncTime,
            TransactionsAdded = result.TransactionsAdded,
            ErrorMessage = result.ErrorMessage
        });
    }

    [HttpGet("status")]
    public async Task<ActionResult<List<SyncStatusDto>>> GetStatus([FromQuery] int take = 10)
    {
        var syncs = await _db.SyncMetadata
            .OrderByDescending(s => s.LastSyncTime)
            .Take(take)
            .Select(s => new SyncStatusDto
            {
                Id = s.Id,
                Status = s.Status,
                LastSyncTime = s.LastSyncTime,
                TransactionsAdded = s.TransactionsAdded,
                ErrorMessage = s.ErrorMessage
            })
            .ToListAsync();

        return Ok(syncs);
    }

    [HttpGet("status/latest")]
    public async Task<ActionResult<SyncStatusDto>> GetLatestStatus()
    {
        var sync = await _db.SyncMetadata
            .OrderByDescending(s => s.LastSyncTime)
            .FirstOrDefaultAsync();

        if (sync == null)
        {
            return NotFound();
        }

        return Ok(new SyncStatusDto
        {
            Id = sync.Id,
            Status = sync.Status,
            LastSyncTime = sync.LastSyncTime,
            TransactionsAdded = sync.TransactionsAdded,
            ErrorMessage = sync.ErrorMessage
        });
    }
}
