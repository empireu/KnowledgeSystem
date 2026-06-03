namespace KnowledgeSystem.Retrieval.Api.Graph;

/// <summary>
///     Represents a property or attribute associated with a single entity.
/// </summary>
public sealed class RawEntityAttribute(
    RawEvidence evidence,
    string entityName,
    string attributeName,
    string value)
{
    /// <summary>
    ///     The evidence for this attribute.
    /// </summary>
    public RawEvidence Evidence { get; } = evidence;

    /// <summary>
    ///     The primary name of the entity this attribute belongs to.
    /// </summary>
    public string EntityName { get; } = entityName;

    /// <summary>
    ///     The attribute or property name (e.g. "talks in sleep", "length", "color").
    /// </summary>
    public string AttributeName { get; } = attributeName;

    /// <summary>
    ///     The literal value of the attribute.
    /// </summary>
    public string Value { get; } = value;
}