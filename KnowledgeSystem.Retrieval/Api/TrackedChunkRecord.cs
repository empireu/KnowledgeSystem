namespace KnowledgeSystem.Retrieval.Api;

/// <summary>
///     Data about a chunk tracked by an <see cref="IIndexStateTracker"/>.
/// </summary>
/// <param name="HashHex">The chunk's hash.</param>
/// <param name="ChunkId">The store-unique chunk ID.</param>
/// <param name="DocumentPath">The document to the path where the chunk originates from.</param>
public record struct TrackedChunkRecord(
    string HashHex,
    int ChunkId,
    string DocumentPath
);