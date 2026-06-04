namespace KnowledgeSystem.Retrieval.Api.Graph;

/// <summary>
///     Ingests <see cref="IngestionChunkSource"/>s to extract entities and claims.
/// </summary>
public interface IFeatureExtractionPipeline
{
    /// <summary>
    ///     Ingests a chunk and produces a chunk-level entity and claim set.
    ///     All chunks are independent across the pipeline; they don't get cross-referenced internally.
    /// </summary>
    public Task<RawProcessedIngestionChunk> IngestAsync(IngestionChunkSource source, CancellationToken cancellationToken = default);
}