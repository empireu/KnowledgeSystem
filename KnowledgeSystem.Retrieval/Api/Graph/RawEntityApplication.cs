namespace KnowledgeSystem.Retrieval.Api.Graph;

/// <summary>
///     Represents a relationship between a source entity and a target entity.
///     All relationships are undirected by default; directionality (if needed) is encoded in the <see cref="ActionDescription"/>.
/// </summary>
public sealed class RawEntityApplication(
    RawEvidence evidence,
    RawExtractedEntity source,
    RawExtractedEntity target,
    string actionDescription
)
{
    public RawEvidence Evidence { get; } = evidence;

    /// <summary>
    ///     The source entity.
    /// </summary>
    public RawExtractedEntity Source { get; } = source;
    /// <summary>
    ///     The target entity.
    /// </summary>
    public RawExtractedEntity Target { get; } = target;
    /// <summary>
    ///     The natural-language description of the connection between the two entities.
    /// </summary>
    public string ActionDescription { get; } = actionDescription;
}