// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace KnowledgeSystem.Plugins.Surveillance.Database;

/// <summary>
///     A raw entity extracted from a chunk, before deduplication.
/// </summary>
public sealed class ExtractedEntityRecord
{
    public long Id { get; set; }

    /// <summary>
    ///     The chunk this entity was extracted from.
    /// </summary>
    public long ChunkId { get; set; }

    /// <summary>
    ///     The primary name of the entity.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>JSON array of alternate names and aliases for this entity.</summary>
    public string AliasesJson { get; set; } = "[]";

    /// <summary>
    ///     The entity type.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    ///     A chunk-level description of the entity.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     The exact evidence text quoted from the source.
    /// </summary>
    public string EvidenceText { get; set; } = null!;

    /// <summary>
    ///     The message that contains the evidence for this entity.
    /// </summary>
    public long SourceMessageId { get; set; }

    /// <summary>
    ///     The chunk this entity was extracted from.
    /// </summary>
    public IngestionChunk Chunk { get; set; } = null!;

    /// <summary>The message that contains the evidence for this entity.</summary>
    public IngestionMessage SourceMessage { get; set; } = null!;
}
