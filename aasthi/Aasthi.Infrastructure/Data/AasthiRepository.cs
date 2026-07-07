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
}
