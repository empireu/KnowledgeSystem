// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace KnowledgeSystem.Plugins.Surveillance.Database;

/// <summary>
///     A chunk of messages fed to the extraction pipeline in a single call.
/// </summary>
public sealed class IngestionChunk
{
    public long Id { get; set; }

    /// <summary>
    ///     The batch this chunk belongs to.
    /// </summary>
    public long BatchId { get; set; }

    /// <summary>
    ///     Order of this chunk within the batch.
    /// </summary>
    public int Sequence { get; set; }

    /// <summary>
    ///     Timestamp of the earliest message in this chunk.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    ///     Timestamp of the latest message in this chunk.
    /// </summary>
    public DateTime EndedAt { get; set; }

    /// <summary>
    ///     Processing status of this chunk.
    /// </summary>
    public ChunkStatus Status { get; set; }

    /// <summary>
    ///     The batch this chunk belongs to.
    /// </summary>
    public IngestionBatch Batch { get; set; } = null!;

    /// <summary>
    ///     Raw entities extracted from this chunk.
    /// </summary>
    public List<ExtractedEntityRecord> Entities { get; set; } = [];

    /// <summary>
    ///     Raw claims extracted from this chunk.
    /// </summary>
    public List<ExtractedClaimRecord> Claims { get; set; } = [];
}
