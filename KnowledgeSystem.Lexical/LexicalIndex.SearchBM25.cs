// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Lexical;

public sealed partial class LexicalIndex
{
    private const float K1 = 1.2f;
    private const float B = 0.75f;
    
    /// <summary>
    ///     Searches for chunks matching the query using BM25.
    /// </summary>
    public Bm25Result[] SearchBm25(string query)
    {
        var frequencyTable = Tokenizer.TokenizeWithFrequency(query, false);

        if (frequencyTable.Count == 0 || TotalChunkCount == 0 || _averageChunkLengthTokens == 0)
        {
            return [];
        }

        var scores = new Dictionary<int, float>();
        foreach (var (term, frequency) in frequencyTable)
        {
            if (!_invertedIndexForChunks.TryGetValue(term, out var postings))
            {
                continue;
            }

            var globalDf = _globalDocumentFrequencies.TryGetValue(term, out var df) 
                ? df 
                : 1;
            
            var idf = MathF.Log((TotalDocumentCount - globalDf + 0.5f) / (globalDf + 0.5f) + 1.0f);
            for (var i = 0; i < postings.Count; i++)
            {
                var posting = postings[i];
                var tfNorm = posting.TermFrequency * (K1 + 1.0f) / (posting.TermFrequency + K1 * (1.0f - B + B * posting.DocumentLength / _averageChunkLengthTokens));
                var score = idf * tfNorm * frequency;
                
                if (!scores.TryGetValue(posting.ChunkId, out var existingScore))
                {
                    scores[posting.ChunkId] = score;
                }
                else
                {
                    scores[posting.ChunkId] = existingScore + score;
                }
            }
        }

        var results = new Bm25Result[scores.Count];
        var index = 0;
        foreach (var (chunkId, score) in scores)
        {
            results[index++] = new Bm25Result(chunkId, score);
        }

        // Sort descending by score, like the other algorithms:
        Array.Sort(results, (a, b) => b.Score.CompareTo(a.Score));

        return results;
    }
}