namespace Aasthi.Application.Interfaces;

public interface IDocumentStorage
{
    Task<string> SaveAsync(Guid propertyId, Guid documentId, string originalFileName, Stream content);
    void Delete(string storagePath);
    string GetFullPath(string storagePath);
}
