using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vault.API.Models;
using Vault.Worker.Data;
using Vault.Worker.Models;

namespace Vault.API.Controllers;

[ApiController]
[Route("api/category-groups")]
public class CategoryGroupController : ControllerBase
{
    private readonly VaultDbContext _db;

    public CategoryGroupController(VaultDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<List<CategoryGroupDto>>> GetAll()
    {
        var groups = await _db.CategoryGroups
            .Include(g => g.Items)
            .OrderBy(g => g.Name)
            .ToListAsync();

        return Ok(groups.Select(ToDto).ToList());
    }

    [HttpGet("{id}/summary")]
    public async Task<ActionResult<CategoryGroupSummaryDto>> GetSummary(string id, [FromQuery] int month = 0, [FromQuery] int year = 0)
    {
        var group = await _db.CategoryGroups.Include(g => g.Items).FirstOrDefaultAsync(g => g.Id == id);
        if (group == null) return NotFound();

        if (month == 0) month = DateTime.UtcNow.Month;
        if (year == 0) year = DateTime.UtcNow.Year;

        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var txns = await _db.Transactions
            .Include(t => t.Account).ThenInclude(a => a!.Institution)
            .Where(t => t.TransactionDate >= monthStart && t.TransactionDate < monthEnd && !t.IsPending)
            .ToListAsync();

        // Match transactions against group items (keyword match on merchant or description)
        var keywords = group.Items.Select(i => i.Keyword.ToLowerInvariant()).ToList();
        var matchedTxns = txns.Where(t =>
        {
            var merchant = (t.MerchantName ?? "").ToLowerInvariant();
            var desc = t.Description.ToLowerInvariant();
            return keywords.Any(k => merchant.Contains(k) || desc.Contains(k));
        }).ToList();

        // Split income vs expense per rule
        decimal totalIncome = 0, totalExpenses = 0;
        var matchedDtos = new List<TransactionDto>();

        foreach (var t in matchedTxns)
        {
            var matchedItem = group.Items.FirstOrDefault(item =>
            {
                var k = item.Keyword.ToLowerInvariant();
                return (t.MerchantName ?? "").ToLowerInvariant().Contains(k) ||
                       t.Description.ToLowerInvariant().Contains(k);
            });

            if (matchedItem?.IsIncome == true)
                totalIncome += Math.Abs(t.Amount);
            else
                totalExpenses += t.Amount > 0 ? t.Amount : Math.Abs(t.Amount);

            matchedDtos.Add(new TransactionDto
            {
                Id = t.Id,
                AccountId = t.AccountId,
                AccountName = t.Account?.Name ?? "",
                InstitutionName = t.Account?.Institution?.Name ?? "",
                Amount = t.Amount,
                Currency = t.Currency,
                TransactionDate = t.TransactionDate,
                Description = t.Description,
                MerchantName = t.MerchantName,
                Category = t.Category,
                IsPending = t.IsPending
            });
        }

        return Ok(new CategoryGroupSummaryDto
        {
            Id = group.Id,
            Name = group.Name,
            Budget = group.Budget,
            Color = group.Color,
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            Net = totalIncome - totalExpenses,
            Transactions = matchedDtos.OrderByDescending(t => t.TransactionDate).ToList()
        });
    }

    [HttpPost]
    public async Task<ActionResult<CategoryGroupDto>> Create([FromBody] CreateCategoryGroupRequest req)
    {
        var group = new CategoryGroup
        {
            Id = Guid.NewGuid().ToString(),
            Name = req.Name,
            Budget = req.Budget,
            Color = req.Color,
            Notes = req.Notes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.CategoryGroups.Add(group);
        await _db.SaveChangesAsync();
        return Ok(ToDto(group));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<CategoryGroupDto>> Update(string id, [FromBody] CreateCategoryGroupRequest req)
    {
        var group = await _db.CategoryGroups.Include(g => g.Items).FirstOrDefaultAsync(g => g.Id == id);
        if (group == null) return NotFound();

        group.Name = req.Name;
        group.Budget = req.Budget;
        group.Color = req.Color;
        group.Notes = req.Notes;
        group.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ToDto(group));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        var group = await _db.CategoryGroups.FindAsync(id);
        if (group == null) return NotFound();
        _db.CategoryGroups.Remove(group);
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("{id}/items")]
    public async Task<ActionResult<CategoryGroupDto>> AddItem(string id, [FromBody] AddCategoryGroupItemRequest req)
    {
        var group = await _db.CategoryGroups.Include(g => g.Items).FirstOrDefaultAsync(g => g.Id == id);
        if (group == null) return NotFound();

        group.Items.Add(new CategoryGroupItem
        {
            Id = Guid.NewGuid().ToString(),
            CategoryGroupId = id,
            Keyword = req.Keyword,
            IsIncome = req.IsIncome,
            Label = req.Label,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return Ok(ToDto(group));
    }

    [HttpDelete("{id}/items/{itemId}")]
    public async Task<ActionResult> RemoveItem(string id, string itemId)
    {
        var item = await _db.CategoryGroupItems.FirstOrDefaultAsync(i => i.Id == itemId && i.CategoryGroupId == id);
        if (item == null) return NotFound();
        _db.CategoryGroupItems.Remove(item);
        await _db.SaveChangesAsync();
        return Ok();
    }

    private static CategoryGroupDto ToDto(CategoryGroup g) => new()
    {
        Id = g.Id,
        Name = g.Name,
        Budget = g.Budget,
        Color = g.Color,
        Notes = g.Notes,
        Items = g.Items.Select(i => new CategoryGroupItemDto
        {
            Id = i.Id,
            Keyword = i.Keyword,
            IsIncome = i.IsIncome,
            Label = i.Label
        }).ToList()
    };
}
