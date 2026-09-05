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

    // Financials
    Task<List<PropertyFinancialEntry>> GetFinancialsAsync(Guid? propertyId = null, string? status = null);
    Task<PropertyFinancialEntry?> GetFinancialAsync(Guid entryId);
    Task<PropertyFinancialEntry> AddFinancialAsync(PropertyFinancialEntry entry);
    Task<bool> UpdateFinancialAsync(PropertyFinancialEntry entry);
    Task<bool> DeleteFinancialAsync(Guid entryId);

    // Which of these Vault transactions are already accounted for -- matched, awaiting
    // confirmation, or explicitly rejected. The daily pass asks this before proposing
    // anything, so a transaction the user has already dealt with is never raised twice.
    Task<HashSet<string>> GetLinkedTransactionIdsAsync(IEnumerable<string> vaultTransactionIds);

    // Recurring charges
    Task<List<RecurringCharge>> GetRecurringChargesAsync(Guid? propertyId = null, bool activeOnly = false);
    Task<RecurringCharge?> GetRecurringChargeAsync(Guid id);
    Task<RecurringCharge> AddRecurringChargeAsync(RecurringCharge charge);
    Task<bool> UpdateRecurringChargeAsync(RecurringCharge charge);
    Task<bool> DeleteRecurringChargeAsync(Guid id);

    // Maintenance
    Task<List<MaintenanceLog>> GetMaintenanceAsync(Guid? propertyId = null);
    Task<MaintenanceLog> AddMaintenanceAsync(MaintenanceLog log);
    Task<bool> DeleteMaintenanceAsync(Guid logId);
}
