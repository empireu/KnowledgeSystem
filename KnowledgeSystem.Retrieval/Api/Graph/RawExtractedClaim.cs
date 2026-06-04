namespace KnowledgeSystem.Retrieval.Api.Graph;

/// <summary>
///     Represents a claim extracted from a text chunk: a subject–predicate–object triple with an associated modality (fact, opinion, speculation, ...).
///     The object is either a named entity (<see cref="ObjectEntity"/>) or a literal value (<see cref="ObjectLiteral"/>), never both.
/// </summary>
public sealed class RawExtractedClaim(
    RawExtractedEntity subject,
    string predicate,
    RawExtractedEntity? objectEntity,
    string? objectLiteral,
    string modality,
    RawEvidence evidence)
{
    /// <summary>
    ///     The entity that the claim is about.
    /// </summary>
    public RawExtractedEntity Subject { get; } = subject;

    /// <summary>
    ///     The predicate or relationship description (e.g. "defeated", "is the best", "has length").
    /// </summary>
    public string Predicate { get; } = predicate;

    /// <summary>
    ///     The object entity, when the claim's object is a named entity.
    ///     Null when the object is a literal value (see <see cref="ObjectLiteral"/>).
    /// </summary>
    public RawExtractedEntity? ObjectEntity { get; } = objectEntity;

    /// <summary>
    ///     The literal object value, when the claim's object is not a named entity (e.g. "red", "very intelligent").
    ///     Null when the object is a named entity (see <see cref="ObjectEntity"/>).
    /// </summary>
    public string? ObjectLiteral { get; } = objectLiteral;

    /// <summary>
    ///     The modality of the claim (e.g. "fact", "opinion", "speculation", "negation", "command", "question", "joke").
    ///     Stored as a string; the valid values are defined by the extraction prompt.
    ///     Can also be something the model came up with.
    /// </summary>
    public string Modality { get; } = modality;

    /// <summary>
    ///     The evidence for the claim, drawn from the source text.
    /// </summary>
    public RawEvidence Evidence { get; } = evidence;
}
