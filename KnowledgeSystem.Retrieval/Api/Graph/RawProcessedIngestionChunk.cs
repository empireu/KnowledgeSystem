namespace KnowledgeSystem.Retrieval.Api.Graph;

public class RawProcessedIngestionChunk(IngestionChunkSource source)
{
    public IngestionChunkSource Source { get; } = source;

    public RawExtractedEntity[] Entities { get; init; } = [];

    public RawEntityApplication[] Relationships { get; init; } = [];

    public RawEntityAttribute[] Attributes { get; init; } = [];
}