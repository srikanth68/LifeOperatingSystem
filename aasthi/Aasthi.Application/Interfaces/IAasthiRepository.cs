using Aasthi.Domain.Entities;

namespace Aasthi.Application.Interfaces;

public interface IAasthiRepository
{
    Task<List<Property>> GetPropertiesAsync();
    Task<Property?> GetPropertyAsync(Guid id);
    Task<Property> AddPropertyAsync(Property property);
    Task<bool> UpdatePropertyAsync(Property property);
    Task<bool> DeletePropertyAsync(Guid id);

    Task<PropertyContact?> AddContactAsync(Guid propertyId, PropertyContact contact);
    Task<bool> UpdateContactAsync(Guid propertyId, PropertyContact contact);
    Task<bool> DeleteContactAsync(Guid propertyId, Guid contactId);

    Task<PropertyDocument?> AddDocumentAsync(Guid propertyId, PropertyDocument document);
    Task<PropertyDocument?> GetDocumentAsync(Guid propertyId, Guid documentId);
    Task<bool> DeleteDocumentAsync(Guid propertyId, Guid documentId);
}
