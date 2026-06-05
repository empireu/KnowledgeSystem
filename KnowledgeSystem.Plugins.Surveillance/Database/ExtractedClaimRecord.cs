// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace KnowledgeSystem.Plugins.Surveillance.Database;

/// <summary>
///     A raw claim extracted from a chunk, before entity resolution.
/// </summary>
public sealed class ExtractedClaimRecord
{
    public long Id { get; set; }

    /// <summary>
    ///     The chunk this claim was extracted from.
    /// </summary>
    public long ChunkId { get; set; }

    /// <summary>
    ///     The subject entity name (unresolved).
    /// </summary>
    public string SubjectName { get; set; } = null!;

    /// <summary>
    ///     The predicate or relationship description.
    /// </summary>
    public string Predicate { get; set; } = null!;

    /// <summary>
    ///     The object entity name, when the object is a named entity.
    /// </summary>
    public string? ObjectEntityName { get; set; }

    /// <summary>
    ///     The literal object value, when the object is not a named entity.
    /// </summary>
    public string? ObjectLiteral { get; set; }

    /// <summary>
    ///     The claim modality (based on the system prompt).
    /// </summary>
    public string Modality { get; set; } = null!;

    /// <summary>
    ///     The exact evidence text quoted from the source.
    /// </summary>
    public string EvidenceText { get; set; } = null!;

    /// <summary>
    ///     The message that contains the evidence for this claim.
    /// </summary>
    public long SourceMessageId { get; set; }

    /// <summary>
    ///     The chunk this claim was extracted from.
    /// </summary>
    public IngestionChunk Chunk { get; set; } = null!;

    /// <summary>
    ///     The message that contains the evidence for this claim.
    /// </summary>
    public IngestionMessage SourceMessage { get; set; } = null!;
}
