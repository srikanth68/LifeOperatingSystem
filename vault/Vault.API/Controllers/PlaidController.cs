using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vault.Worker.Data;
using Vault.Worker.Models;
using Vault.Worker.Services;

namespace Vault.API.Controllers;

[ApiController]
[Route("api/plaid")]
public class PlaidController : ControllerBase
{
    private readonly IPlaidService _plaidService;
    private readonly ISyncService _syncService;
    private readonly VaultDbContext _db;
    private readonly ILogger<PlaidController> _logger;

    public PlaidController(IPlaidService plaidService, ISyncService syncService, VaultDbContext db, ILogger<PlaidController> logger)
    {
        _plaidService = plaidService;
        _syncService = syncService;
        _db = db;
        _logger = logger;
    }

    [HttpPost("link-token")]
    public async Task<ActionResult<object>> CreateLinkToken()
    {
        try
        {
            var linkToken = await _plaidService.CreateLinkTokenAsync();
            return Ok(new { linkToken });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create link token");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("exchange-token")]
    public async Task<ActionResult<object>> ExchangeToken([FromBody] ExchangeTokenRequest req)
    {
        try
        {
            var (accessToken, itemId) = await _plaidService.ExchangePublicTokenAsync(req.PublicToken);

            var existing = await _db.PlaidItems.FirstOrDefaultAsync(i => i.PlaidItemId == itemId);
            if (existing == null)
            {
                _db.PlaidItems.Add(new PlaidItem
                {
                    Id = Guid.NewGuid().ToString(),
                    PlaidItemId = itemId,
                    AccessToken = accessToken,
                    PlaidInstitutionId = req.PlaidInstitutionId,
                    InstitutionName = req.InstitutionName,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
                _logger.LogInformation("Linked new institution: {Name}", req.InstitutionName);
            }

            // Kick off a sync immediately
            var syncResult = await _syncService.SyncAsync();

            return Ok(new
            {
                success = true,
                institutionName = req.InstitutionName,
                syncStatus = syncResult.Status,
                transactionsAdded = syncResult.TransactionsAdded
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token exchange failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("items")]
    public async Task<ActionResult<object>> GetLinkedItems()
    {
        var items = await _db.PlaidItems
            .Where(i => i.IsActive)
            .Select(i => new
            {
                i.Id,
                i.InstitutionName,
                i.PlaidInstitutionId,
                i.CreatedAt
            })
            .ToListAsync();

        return Ok(items);
    }

    [HttpDelete("items/{id}")]
    public async Task<ActionResult> UnlinkItem(string id)
    {
        var item = await _db.PlaidItems.FindAsync(id);
        if (item == null) return NotFound();

        item.IsActive = false;
        item.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Unlinked institution: {Name}", item.InstitutionName);
        return Ok(new { success = true });
    }
}

public record ExchangeTokenRequest(string PublicToken, string PlaidInstitutionId, string InstitutionName);
