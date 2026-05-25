namespace KnowledgeSystem.Retrieval.Data;

/// <summary>
///     Tracks the sync state of indexed documents and chunks.
///     Used to diff the current repository against what was last indexed.
/// </summary>
public interface IIndexStateTracker
{
    /// <summary>
    ///     Prepares the tracker for use (e.g. ensures database exists for persistent implementations).
    ///     Must be called before any other operations.
    /// </summary>
    Task PrepareForUseAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    ///     Gets all known document paths.
    /// </summary>
    HashSet<string> GetKnownDocumentPaths();

    /// <summary>
    ///     Gets the hashes of all known chunks for a document.
    /// </summary>
    HashSet<string> GetKnownChunkHashes(string documentPath);

    /// <summary>
    ///     Tries to get the chunk ID for a given hash.
    /// </summary>
    bool TryGetChunkIdByHash(string hashHex, out int chunkId);

    /// <summary>
    ///     Records a document as indexed.
    /// </summary>
    void AddDocument(string path);

    /// <summary>
    ///     Records a chunk as indexed with its ID.
    /// </summary>
    void AddChunk(string hashHex, int chunkId, string documentPath);

    /// <summary>
    ///     Removes a document and all its chunks.
    /// </summary>
    void RemoveDocument(string path);

    /// <summary>
    ///     Removes a specific chunk by hash.
    /// </summary>
    void RemoveChunk(string hashHex);

    /// <summary>
    ///     Gets all chunk records (hash → chunkId + documentPath).
    /// </summary>
    IReadOnlyList<(string HashHex, int ChunkId, string DocumentPath)> GetAllChunkRecords();

    /// <summary>
    ///     Persists changes. No-op for in-memory implementations.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
