namespace KnowledgeSystem.Plugins.Surveillance.Extraction;

/// <summary>
///     A chunk of messages formatted for the extraction pipeline, with metadata to map evidence spans back to messages.
/// </summary>
public sealed class ChunkedMessages
{
    /// <summary>
    ///     The formatted text fed to the extraction LLM (one line per message).
    /// </summary>
    public string SourceContent { get; init; } = null!;

    /// <summary>
    ///     Line index to Discord message mappings, one per line in <see cref="SourceContent"/>.
    /// </summary>
    public List<MessageLineMapping> LineMappings { get; init; } = [];

    /// <summary>
    ///     Timestamp of the earliest message in this chunk.
    /// </summary>
    public DateTime StartedAt { get; init; }

    /// <summary>
    ///     Timestamp of the latest message in this chunk.
    /// </summary>
    public DateTime EndedAt { get; init; }
}
