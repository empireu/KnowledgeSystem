using KnowledgeSystem.EmdParser.ExtendedMarkdown;

// ReSharper disable EntityFramework.ModelValidation.UnlimitedStringLength
// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace KnowledgeSystem.Retrieval.Persistent;

/// <summary>
///     Represents a single chunk tracked in the RAG database.
///     Maps a chunk hash to its integer ID in the HNSW index.
/// </summary>
public class ChunkRecord
{
    /// <summary>
    ///     The hex-encoded SHA256 hash of the chunk, produced by <see cref="EmdChunkHash.ToHexString"/>.
    ///     This is the primary key.
    /// </summary>
    public string HashHex { get; set; } = null!;

    /// <summary>
    ///     The integer ID of this chunk, unique within the store.
    /// </summary>
    public int ChunkId { get; set; }

    /// <summary>
    ///     The repository-relative path of the document containing this chunk.
    ///     Foreign key to <see cref="DocumentRecord.Path"/>.
    /// </summary>
    public string DocumentPath { get; set; } = null!;

    /// <summary>
    ///     Navigation to the parent document.
    /// </summary>
    public DocumentRecord Document { get; set; } = null!;
}
