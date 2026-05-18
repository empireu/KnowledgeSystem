using KnowledgeSystem.EmdParser.ExtendedMarkdown;

// ReSharper disable ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
// ReSharper disable LoopCanBeConvertedToQuery
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Retrieval.Lexical;

/// <summary>
///     Inverted index for keyword search over chunks.
///     Built from chunk text at engine initialization time.
/// </summary>
public sealed partial class LexicalIndex
{
    private readonly Dictionary<string, List<Entry>> _invertedIndex = [];
    private float _averageDocumentLengthTokens;

    private readonly struct Entry(int hnswId, int termFrequency, int documentLength)
    {
        public readonly int HnswId = hnswId;
        public readonly int TermFrequency = termFrequency;
        public readonly int DocumentLength = documentLength;
    }

    /// <summary>
    ///     The total number of chunks in the index.
    /// </summary>
    public int TotalChunkCount { get; private set; }
    
    /// <summary>
    ///     Builds the inverted index from the given chunks.
    /// </summary>
    public void Build(IReadOnlyDictionary<int, EmdChunk> chunksByHnswId)
    {
        _invertedIndex.Clear();
        TotalChunkCount = chunksByHnswId.Count;

        var totalDocumentLengthTokens = 0;

        foreach (var (hnswId, chunk) in chunksByHnswId)
        {
            var frequencies = Tokenizer.TokenizeQueryFrequency(chunk.RawContent, false);

            var documentLength = 0;
            foreach (var frequency in frequencies.Values)
            {
                documentLength += frequency;
            }
            
            totalDocumentLengthTokens += documentLength;

            foreach (var (term, frequency) in frequencies)
            {
                if (!_invertedIndex.TryGetValue(term, out var postings))
                {
                    postings = [];
                    _invertedIndex.Add(term, postings);
                }

                postings.Add(new Entry(hnswId, frequency, documentLength));
            }
        }

        _averageDocumentLengthTokens = TotalChunkCount > 0 
            ? (float)totalDocumentLengthTokens / TotalChunkCount 
            : 0;
    }

    /// <summary>
    ///     Returns the number of chunks containing the given term, or 0 if the term is not in the index.
    /// </summary>
    public int GetChunkFrequency(string term)
    {
        return _invertedIndex.TryGetValue(term.ToLowerInvariant(), out var postings) 
            ? postings.Count
            : 0;
    }
}
