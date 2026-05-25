namespace KnowledgeSystem.Lexical;

/// <summary>
///     Result of a keyword search.
/// </summary>
/// <param name="hnswId">The HNSW vector index of the matching chunk.</param>
/// <param name="score">The BM25 score, where higher is better (unlike the modified cosine similarity).</param>
public readonly struct Bm25Result(int hnswId, float score)
{
    public readonly int HnswId = hnswId;
    public readonly float Score = score;
}