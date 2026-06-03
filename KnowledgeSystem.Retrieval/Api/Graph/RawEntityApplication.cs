namespace KnowledgeSystem.Retrieval.Api.Graph;

/// <summary>
///     Represents an application performed by the source entity on the target entity, or a relationship between the two.
/// </summary>
public sealed class RawEntityApplication(
    RawEvidence evidence,
    RawExtractedEntity source,
    RawExtractedEntity target,
    string actionDescription,
    RawRelationshipType relationshipType
)
{
    public RawEvidence Evidence { get; } = evidence;

    /// <summary>
    ///     The source entity performing the <see cref="ActionDescription"/>.
    /// </summary>
    public RawExtractedEntity Source { get; } = source;
    /// <summary>
    ///     The target entity the <see cref="ActionDescription"/> is being performed on.
    /// </summary>
    public RawExtractedEntity Target { get; } = target;
    /// <summary>
    ///     The natural-language representation of the performed action.
    /// </summary>
    public string ActionDescription { get; } = actionDescription;
    /// <summary>
    ///     The type of relationship between the two sides.
    /// </summary>
    public RawRelationshipType RelationshipType { get; } = relationshipType;
}