using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vault.API.Models;
using Vault.Worker.Data;

namespace Vault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountsController : ControllerBase
{
    private readonly VaultDbContext _db;
    private readonly ILogger<AccountsController> _logger;

    public AccountsController(VaultDbContext db, ILogger<AccountsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<AccountDto>>> GetAll([FromQuery] string? institutionId = null)
    {
        _logger.LogInformation($"Fetching accounts {(institutionId != null ? $"for institution {institutionId}" : "")}");

        var query = _db.Accounts.Include(a => a.Institution).AsQueryable();

        if (!string.IsNullOrEmpty(institutionId))
        {
            query = query.Where(a => a.InstitutionId == institutionId);
        }

        var accounts = await query
            .Select(a => new AccountDto
            {
                Id = a.Id,
                InstitutionId = a.InstitutionId,
                InstitutionName = a.Institution!.Name,
                Name = a.Name,
                Type = a.Type,
                SubType = a.SubType,
                Balance = a.Balance,
                AvailableBalance = a.AvailableBalance,
                Currency = a.Currency,
                IsActive = a.IsActive
            })
            .ToListAsync();

        return Ok(accounts);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AccountDto>> GetById(string id)
    {
        var account = await _db.Accounts.Include(a => a.Institution).FirstOrDefaultAsync(a => a.Id == id);
        if (account == null)
        {
            return NotFound();
        }

        return Ok(new AccountDto
        {
            Id = account.Id,
            InstitutionId = account.InstitutionId,
            InstitutionName = account.Institution?.Name ?? "",
            Name = account.Name,
            Type = account.Type,
            SubType = account.SubType,
            Balance = account.Balance,
            AvailableBalance = account.AvailableBalance,
            Currency = account.Currency,
            IsActive = account.IsActive
        });
    }
}
