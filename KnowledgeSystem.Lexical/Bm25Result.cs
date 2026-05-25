namespace KnowledgeSystem.Lexical;

/// <summary>
///     Result of a keyword search.
/// </summary>
/// <param name="chunkId">The unique ID of the matching chunk within the store.</param>
/// <param name="score">The BM25 score, where higher is better (unlike the modified cosine similarity).</param>
public readonly struct Bm25Result(int chunkId, float score)
{
    public readonly int ChunkId = chunkId;
    public readonly float Score = score;
}