namespace KnowledgeSystem.Retrieval.Api.Graph;

/// <summary>
///     Represents an entity extracted from a text chunk.
/// </summary>
public class RawExtractedEntity(RawEvidence evidence, string[] definedNames, string? description, string? type)
{
    public RawEvidence Evidence { get; } = evidence;

    /// <summary>
    ///     The names for the entity, assigned in the chunk.
    ///     If the entity shows up multiple times under different names, all the names will be extracted here.
    /// </summary>
    public string[] DefinedNames { get; } = definedNames;
    /// <summary>
    ///     The chunk-level description of the entity, if it can be inferred.
    /// </summary>
    public string? Description { get; } = description;
    /// <summary>
    ///     The chunk-level entity type, if it can be inferred.
    /// </summary>
    public string? Type { get; } = type;
}