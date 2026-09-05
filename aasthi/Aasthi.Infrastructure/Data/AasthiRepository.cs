using Microsoft.EntityFrameworkCore;
using Aasthi.Application.Interfaces;
using Aasthi.Domain.Entities;

namespace Aasthi.Infrastructure.Data;

public class AasthiRepository(AasthiDbContext db) : IAasthiRepository
{
    public async Task<List<Property>> GetPropertiesAsync() =>
        await db.Properties
            .Include(p => p.Contacts)
            .Include(p => p.Documents)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

    public async Task<Property?> GetPropertyAsync(Guid id) =>
        await db.Properties
            .Include(p => p.Contacts)
            .Include(p => p.Documents)
            .FirstOrDefaultAsync(p => p.Id == id);

    public async Task<Property> AddPropertyAsync(Property property)
    {
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        return property;
    }

    public async Task<bool> UpdatePropertyAsync(Property property)
    {
        var existing = await db.Properties.FirstOrDefaultAsync(p => p.Id == property.Id);
        if (existing is null) return false;

        existing.Address = property.Address;
        existing.City = property.City;
        existing.State = property.State;
        existing.Zip = property.Zip;
        existing.Country = property.Country;
        existing.Latitude = property.Latitude;
        existing.Longitude = property.Longitude;
        existing.PurchasePrice = property.PurchasePrice;
        existing.PurchaseDate = property.PurchaseDate;
        existing.CurrentValue = property.CurrentValue;
        existing.CurrentValueAsOf = property.CurrentValueAsOf;
        existing.Notes = property.Notes;

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeletePropertyAsync(Guid id)
    {
        var existing = await db.Properties.FirstOrDefaultAsync(p => p.Id == id);
        if (existing is null) return false;
        db.Properties.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<PropertyContact?> AddContactAsync(Guid propertyId, PropertyContact contact)
    {
        var exists = await db.Properties.AnyAsync(p => p.Id == propertyId);
        if (!exists) return null;
        contact.PropertyId = propertyId;
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();
        return contact;
    }

    public async Task<bool> UpdateContactAsync(Guid propertyId, PropertyContact contact)
    {
        var existing = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contact.Id && c.PropertyId == propertyId);
        if (existing is null) return false;
        existing.Name = contact.Name;
        existing.Role = contact.Role;
        existing.Phone = contact.Phone;
        existing.Email = contact.Email;
        existing.Notes = contact.Notes;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteContactAsync(Guid propertyId, Guid contactId)
    {
        var existing = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId && c.PropertyId == propertyId);
        if (existing is null) return false;
        db.Contacts.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<PropertyDocument?> AddDocumentAsync(Guid propertyId, PropertyDocument document)
    {
        var exists = await db.Properties.AnyAsync(p => p.Id == propertyId);
        if (!exists) return null;
        document.PropertyId = propertyId;
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return document;
    }

    public async Task<PropertyDocument?> GetDocumentAsync(Guid propertyId, Guid documentId) =>
        await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.PropertyId == propertyId);

    public async Task<bool> DeleteDocumentAsync(Guid propertyId, Guid documentId)
    {
        var existing = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.PropertyId == propertyId);
        if (existing is null) return false;
        db.Documents.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<PropertyTask>> GetTasksAsync(Guid? propertyId = null, string? status = null)
    {
        var q = db.Tasks.AsQueryable();
        if (propertyId.HasValue) q = q.Where(t => t.PropertyId == propertyId.Value);
        if (!string.IsNullOrEmpty(status)) q = q.Where(t => t.Status == status);
        return await q.OrderByDescending(t => t.Priority == "urgent" ? 0 : t.Priority == "high" ? 1 : t.Priority == "medium" ? 2 : 3)
                      .ThenBy(t => t.DueDate)
                      .ThenByDescending(t => t.CreatedAt)
                      .ToListAsync();
    }

    public async Task<PropertyTask?> GetTaskAsync(Guid taskId) =>
        await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId);

    public async Task<PropertyTask> AddTaskAsync(PropertyTask task)
    {
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    public async Task<bool> UpdateTaskAsync(PropertyTask task)
    {
        var existing = await db.Tasks.FirstOrDefaultAsync(t => t.Id == task.Id);
        if (existing is null) return false;
        existing.Title = task.Title;
        existing.Description = task.Description;
        existing.DueDate = task.DueDate;
        existing.Status = task.Status;
        existing.Priority = task.Priority;
        existing.Source = task.Source;
        existing.CompletedAt = task.CompletedAt;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteTaskAsync(Guid taskId)
    {
        var existing = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (existing is null) return false;
        db.Tasks.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Financials ──
    public async Task<List<PropertyFinancialEntry>> GetFinancialsAsync(Guid? propertyId = null, string? status = null)
    {
        var q = db.FinancialEntries.AsQueryable();
        if (propertyId.HasValue) q = q.Where(f => f.PropertyId == propertyId.Value);
        // Rejected rows are tombstones, not history: they exist so the daily pass stops
        // re-proposing a transaction, and showing them in the ledger would be noise. A
        // caller that genuinely wants them asks for them by name.
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(f => f.Status == status);
        else q = q.Where(f => f.Status != "rejected");
        return await q.OrderByDescending(f => f.Date).ThenByDescending(f => f.CreatedAt).ToListAsync();
    }

    public async Task<PropertyFinancialEntry?> GetFinancialAsync(Guid entryId) =>
        await db.FinancialEntries.FirstOrDefaultAsync(f => f.Id == entryId);

    public async Task<bool> UpdateFinancialAsync(PropertyFinancialEntry entry)
    {
        var existing = await db.FinancialEntries.FirstOrDefaultAsync(f => f.Id == entry.Id);
        if (existing is null) return false;

        existing.PropertyId = entry.PropertyId;
        existing.Type = entry.Type;
        existing.Category = entry.Category;
        existing.Amount = entry.Amount;
        existing.Date = entry.Date;
        existing.Notes = entry.Notes;
        existing.VaultTransactionId = entry.VaultTransactionId;
        existing.Origin = entry.Origin;
        existing.Status = entry.Status;
        existing.RecurringChargeId = entry.RecurringChargeId;
        existing.MatchConfidence = entry.MatchConfidence;
        existing.TaxTreatment = entry.TaxTreatment;
        existing.ReceiptDocumentId = entry.ReceiptDocumentId;
        existing.ConfirmedAt = entry.ConfirmedAt;

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<HashSet<string>> GetLinkedTransactionIdsAsync(IEnumerable<string> vaultTransactionIds)
    {
        var ids = vaultTransactionIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        if (ids.Count == 0) return [];

        var found = await db.FinancialEntries
            .Where(f => f.VaultTransactionId != null && ids.Contains(f.VaultTransactionId))
            .Select(f => f.VaultTransactionId!)
            .ToListAsync();

        return found.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<List<RecurringCharge>> GetRecurringChargesAsync(Guid? propertyId = null, bool activeOnly = false)
    {
        var q = db.RecurringCharges.AsQueryable();
        if (propertyId.HasValue) q = q.Where(c => c.PropertyId == propertyId.Value);
        if (activeOnly) q = q.Where(c => c.Active);
        return await q.OrderBy(c => c.Category).ThenBy(c => c.DueDay).ToListAsync();
    }

    public async Task<RecurringCharge?> GetRecurringChargeAsync(Guid id) =>
        await db.RecurringCharges.FirstOrDefaultAsync(c => c.Id == id);

    public async Task<RecurringCharge> AddRecurringChargeAsync(RecurringCharge charge)
    {
        db.RecurringCharges.Add(charge);
        await db.SaveChangesAsync();
        return charge;
    }

    public async Task<bool> UpdateRecurringChargeAsync(RecurringCharge charge)
    {
        var existing = await db.RecurringCharges.FirstOrDefaultAsync(c => c.Id == charge.Id);
        if (existing is null) return false;

        existing.PropertyId = charge.PropertyId;
        existing.Direction = charge.Direction;
        existing.Category = charge.Category;
        existing.Amount = charge.Amount;
        existing.Frequency = charge.Frequency;
        existing.DueDay = charge.DueDay;
        existing.StartDate = charge.StartDate;
        existing.EndDate = charge.EndDate;
        existing.MatchHint = charge.MatchHint;
        existing.Active = charge.Active;
        existing.Notes = charge.Notes;

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteRecurringChargeAsync(Guid id)
    {
        var existing = await db.RecurringCharges.FirstOrDefaultAsync(c => c.Id == id);
        if (existing is null) return false;
        db.RecurringCharges.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<PropertyFinancialEntry> AddFinancialAsync(PropertyFinancialEntry entry)
    {
        db.FinancialEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    public async Task<bool> DeleteFinancialAsync(Guid entryId)
    {
        var existing = await db.FinancialEntries.FirstOrDefaultAsync(f => f.Id == entryId);
        if (existing is null) return false;
        db.FinancialEntries.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Maintenance ──
    public async Task<List<MaintenanceLog>> GetMaintenanceAsync(Guid? propertyId = null)
    {
        var q = db.MaintenanceLogs.AsQueryable();
        if (propertyId.HasValue) q = q.Where(m => m.PropertyId == propertyId.Value);
        return await q.OrderByDescending(m => m.CompletedDate).ThenByDescending(m => m.CreatedAt).ToListAsync();
    }

    public async Task<MaintenanceLog> AddMaintenanceAsync(MaintenanceLog log)
    {
        db.MaintenanceLogs.Add(log);
        await db.SaveChangesAsync();
        return log;
    }

    public async Task<bool> DeleteMaintenanceAsync(Guid logId)
    {
        var existing = await db.MaintenanceLogs.FirstOrDefaultAsync(m => m.Id == logId);
        if (existing is null) return false;
        db.MaintenanceLogs.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }
}
