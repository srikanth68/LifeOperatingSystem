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
}
