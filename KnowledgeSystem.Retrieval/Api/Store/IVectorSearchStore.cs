using KnowledgeSystem.Embedding;
using KnowledgeSystem.Vector;

namespace KnowledgeSystem.Retrieval.Api.Store;

public interface IVectorSearchStore : IReadOnlyDocumentStore
{
    /// <summary>
    ///     Gets the embedding service used for vector search.
    /// </summary>
    public IEmbeddingService EmbeddingService { get; }

    /// <summary>
    ///     Gets the vector store backing the search methods.
    /// </summary>
    IReadOnlyVectorStore VectorStore { get; }
    
    /// <summary>
    ///     Embeds and searches for the <paramref name="k"/> chunks most similar to the query text.
    /// </summary>
    public Task<VectorSearchResult[]> SearchAsync(string query, int k, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Embeds and searches for the <paramref name="k"/> chunks most similar to the query text batch.
    /// </summary>
    public Task<VectorSearchResult[][]> SearchAsync(string[] queries, int k, CancellationToken cancellationToken = default);
}