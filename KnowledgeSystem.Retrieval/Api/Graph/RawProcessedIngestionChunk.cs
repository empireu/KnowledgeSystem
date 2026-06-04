namespace KnowledgeSystem.Retrieval.Api.Graph;

public class RawProcessedIngestionChunk(string sourceContent)
{
    public string SourceContent { get; } = sourceContent;

    public RawExtractedEntity[] Entities { get; init; } = [];

    public RawExtractedClaim[] Claims { get; init; } = [];
}