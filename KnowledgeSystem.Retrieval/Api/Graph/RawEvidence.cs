namespace KnowledgeSystem.Retrieval.Api.Graph;

/// <summary>
///     Evidence for an extracted entity or relationship.
/// </summary>
public sealed class RawEvidence(
    string quotedText,
    string sourceText,
    TextSpan? span)
{
    /// <summary>
    ///     The exact text the LLM quoted as evidence for this extraction.
    /// </summary>
    public string QuotedText { get; } = quotedText;

    /// <summary>
    ///     The full source text that was processed (the chunk content).
    /// </summary>
    public string SourceText { get; } = sourceText;
    
    /// <summary>
    ///     Offset range of the evidence in <see cref="SourceText"/>, if fuzzy matching succeeded.
    /// </summary>
    public TextSpan? Span { get; } = span;
}