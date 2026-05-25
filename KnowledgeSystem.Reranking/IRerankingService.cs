namespace KnowledgeSystem.Reranking;

/// <summary>
///     Abstraction over a result reranking service.
/// </summary>
public interface IRerankingService
{
    /// <summary>
    ///     Reranks the given <paramref name="documents"/> and returns the <see cref="topN"/> best results.
    /// </summary>
    /// <param name="query">The query to rank with.</param>
    /// <param name="documents">The raw search results.</param>
    /// <param name="topN">The number of top documents to keep.</param>
    /// <returns>The results, sorted descending by score, if the request was successful. Otherwise, false.</returns>
    Task<RerankResult[]?> RerankAsync(string query, List<string> documents, int topN);
}