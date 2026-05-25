using KnowledgeSystem.EmdParser.ExtendedMarkdown;

namespace KnowledgeSystem.Retrieval.Api;

public interface IDocumentStore : IReadOnlyDocumentStore
{
    /// <summary>
    ///     Adds a document to the store. This can result in many heavy operations, depending on the capabilities.
    /// </summary>
    public Task AddDocument(EmdDocument document);
    
    /// <summary>
    ///     Removes a document from the store. This can result in many heavy operations, depending on the capabilities.
    /// </summary>
    /// <param name="document"></param>
    /// <returns></returns>
    public Task RemoveDocument(EmdDocument document);
}