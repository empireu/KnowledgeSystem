namespace KnowledgeSystem.Retrieval.Api.Graph;

/// <summary>
///     Represents an input chunk for entity and claim extraction.
/// </summary>
public sealed class IngestionChunkSource(string content)
{
    /// <summary>
    ///     The raw text content of this chunk.
    /// </summary>
    public string Content { get; } = content;
}