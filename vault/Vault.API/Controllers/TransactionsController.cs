using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vault.API.Models;
using Vault.Worker.Data;

namespace Vault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TransactionsController : ControllerBase
{
    private readonly VaultDbContext _db;
    private readonly ILogger<TransactionsController> _logger;

    public TransactionsController(VaultDbContext db, ILogger<TransactionsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<TransactionDto>>> GetAll(
        [FromQuery] string? accountId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] string? category = null)
    {
        _logger.LogInformation("Fetching transactions with filters");

        var query = _db.Transactions.Include(t => t.Account).ThenInclude(a => a!.Institution).AsQueryable();

        if (!string.IsNullOrEmpty(accountId))
        {
            query = query.Where(t => t.AccountId == accountId);
        }

        if (startDate.HasValue)
        {
            query = query.Where(t => t.TransactionDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            var endOfDay = endDate.Value.AddDays(1);
            query = query.Where(t => t.TransactionDate < endOfDay);
        }

        if (!string.IsNullOrEmpty(category))
        {
            query = query.Where(t => t.Category == category);
        }

        var transactions = await query
            .OrderByDescending(t => t.TransactionDate)
            .Select(t => new TransactionDto
            {
                Id = t.Id,
                AccountId = t.AccountId,
                AccountName = t.Account != null ? t.Account.Name : "",
                InstitutionName = t.Account != null && t.Account.Institution != null ? t.Account.Institution.Name : "",
                Amount = t.Amount,
                Currency = t.Currency,
                TransactionDate = t.TransactionDate,
                Description = t.Description,
                MerchantName = t.MerchantName,
                Category = t.Category,
                IsPending = t.IsPending
            })
            .ToListAsync();

        return Ok(transactions);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TransactionDto>> GetById(string id)
    {
        var transaction = await _db.Transactions.FindAsync(id);
        if (transaction == null)
        {
            return NotFound();
        }

        return Ok(new TransactionDto
        {
            Id = transaction.Id,
            AccountId = transaction.AccountId,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            TransactionDate = transaction.TransactionDate,
            Description = transaction.Description,
            MerchantName = transaction.MerchantName,
            Category = transaction.Category,
            IsPending = transaction.IsPending
        });
    }
}
