namespace KnowledgeSystem.Retrieval.Lexical;

public sealed partial class LexicalIndex
{
    private const float K1 = 1.2f;
    private const float B = 0.75f;
    
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
    
    /// <summary>
    ///     Searches for chunks matching the query using BM25.
    /// </summary>
    public Bm25Result[] SearchBm25(string query)
    {
        var frequencyTable = Tokenizer.TokenizeQueryFrequency(query, false);

        if (frequencyTable.Count == 0 || TotalChunkCount == 0 || _averageDocumentLengthTokens == 0)
        {
            return [];
        }

        var scores = new Dictionary<int, float>();
        foreach (var (term, frequency) in frequencyTable)
        {
            if (!_invertedIndex.TryGetValue(term, out var postings))
            {
                continue;
            }

            var df = postings.Count;
            var idf = MathF.Log((TotalChunkCount - df + 0.5f) / (df + 0.5f) + 1.0f);

            for (var i = 0; i < postings.Count; i++)
            {
                var posting = postings[i];
                var tfNorm = posting.TermFrequency * (K1 + 1.0f) / (posting.TermFrequency +
                                                                    K1 * (1.0f - B + B * posting.DocumentLength /
                                                                        _averageDocumentLengthTokens));
                var score = idf * tfNorm * frequency;

                if (!scores.TryGetValue(posting.HnswId, out var existingScore))
                {
                    scores[posting.HnswId] = score;
                }
                else
                {
                    scores[posting.HnswId] = existingScore + score;
                }
            }
        }

        var results = new Bm25Result[scores.Count];
        var index = 0;
        foreach (var (hnswId, score) in scores)
        {
            results[index++] = new Bm25Result(hnswId, score);
        }

        // Sort descending by score, like the other algorithms:
        Array.Sort(results, (a, b) => b.Score.CompareTo(a.Score));

        return results;
    }
}