namespace KnowledgeSystem.Retrieval.Api.Graph;

/// <summary>
///     Represents an input chunk for the entity and relationship extraction.
///     It is mainly composed of its text content, but we attach metadata as well, so we know where it came from, and we can also use the metadata when we enter the entities and relationships into the database.
/// </summary>
/// <param name="content"></param>
/// <param name="metadata"></param>
public sealed class IngestionChunkSource(string content, IChunkMetadata[] metadata)
{
    /// <summary>
    ///     The raw text content of this chunk.
    /// </summary>
    public string Content { get; } = content;
    
    /// <summary>
    ///     Metadata fields. They should contain all information we can extract at the source (date, usernames, channel, and whatever else).
    /// </summary>
    public IChunkMetadata[] Metadata { get; } = metadata;
}