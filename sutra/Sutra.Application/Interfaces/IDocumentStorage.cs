namespace Sutra.Application.Interfaces;

public interface IDocumentStorage
{
    Task<string> SaveAsync(string category, Guid documentId, string originalFileName, Stream content);
    void Delete(string storagePath);
    string GetFullPath(string storagePath);
}
