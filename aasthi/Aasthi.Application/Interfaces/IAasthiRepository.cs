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

    // Tasks
    Task<List<PropertyTask>> GetTasksAsync(Guid? propertyId = null, string? status = null);
    Task<PropertyTask?> GetTaskAsync(Guid taskId);
    Task<PropertyTask> AddTaskAsync(PropertyTask task);
    Task<bool> UpdateTaskAsync(PropertyTask task);
    Task<bool> DeleteTaskAsync(Guid taskId);
}
