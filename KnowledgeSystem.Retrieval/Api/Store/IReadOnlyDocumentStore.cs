using System.Diagnostics.CodeAnalysis;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;

namespace KnowledgeSystem.Retrieval.Api.Store;

public interface IReadOnlyDocumentStore : IAsyncDisposable
{
    /// <summary>
    ///     Whether this store supports the given capability.
    /// </summary>
    bool HasCapability(StoreCapabilityType capabilityType);

    /// <summary>
    ///     Gets a capability by its type. Throws if the capability is not supported.
    /// </summary>
    T GetCapability<T>(StoreCapabilityType capabilityType) where T : ISearchCapability;

    /// <summary>
    ///     Unique ID for this store.
    /// </summary>
    string StoreId { get; }
    
    /// <summary>
    ///     Gets the stored documents.
    /// </summary>
    /// <returns>A list of the documents stored.</returns>
    public IReadOnlySet<EmdDocument> ListDocuments();

    /// <summary>
    ///     Tries to get a document by its path.
    /// </summary>
    /// <returns>True if the document was found. Otherwise, false.</returns>
    public bool TryGetDocumentByPath(string path, [NotNullWhen(true)] out EmdDocument? document);

    /// <summary>
    ///     Gets a document by its path.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown if the document was not found.</exception>
    public EmdDocument GetDocumentByPath(string path)
    {
        return TryGetDocumentByPath(path, out var document)
            ? document
            : throw new KeyNotFoundException($"Document with path \"{path}\" not found");
    }

    /// <summary>
    ///     Tries to get a chunk by its unique ID.
    /// </summary>
    /// <returns>True if the chunk was found. Otherwise, false.</returns>
    public bool TryGetChunk(int chunkId, [NotNullWhen(true)] out EmdChunk? chunk);

    /// <summary>
    ///     Gets a chunk by its ID.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown if the chunk was not found.</exception>
    public EmdChunk GetChunk(int chunkId)
    {
        return TryGetChunk(chunkId, out var chunk)
            ? chunk
            : throw new KeyNotFoundException($"Chunk with ID {chunkId} not found");
    }
}