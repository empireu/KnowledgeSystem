using KnowledgeSystem.EmdParser.ExtendedMarkdown;

// ReSharper disable ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
// ReSharper disable LoopCanBeConvertedToQuery
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Lexical;

/// <summary>
///     Inverted index for keyword search over chunks.
///     Built from chunk text at engine initialization time.
/// </summary>
public sealed partial class LexicalIndex
{
    private readonly Dictionary<string, List<ChunkEntry>> _invertedIndexForChunks = new(StringComparer.OrdinalIgnoreCase);
    
    // The number of EMD documents that contain each term:
    private readonly Dictionary<string, int> _globalDocumentFrequencies = new(StringComparer.OrdinalIgnoreCase);    
    
    private float _averageChunkLengthTokens;

    // Pre-tokenized raw chunk text.
    private readonly Dictionary<int, Dictionary<string, int>> _chunkTokensByChunkId = [];

    private readonly struct ChunkEntry(int chunkId, int termFrequency, int documentLength)
    {
        public readonly int ChunkId = chunkId;
        public readonly int TermFrequency = termFrequency;
        public readonly int DocumentLength = documentLength;
    }

    public int TotalDocumentCount { get; private set; }
    
    /// <summary>
    ///     The total number of chunks in the index.
    /// </summary>
    public int TotalChunkCount { get; private set; }
    
    /// <summary>
    ///     Builds the inverted index from the given chunks.
    /// </summary>
    public void Build(int documentCount, IReadOnlyDictionary<int, EmdChunk> chunksByChunkId)
    {
        _invertedIndexForChunks.Clear();
        _globalDocumentFrequencies.Clear();
        _chunkTokensByChunkId.Clear();

        TotalDocumentCount = documentCount;
        TotalChunkCount = chunksByChunkId.Count;
        
        var totalDocumentLengthTokens = 0;
        var parentDocuments = new Dictionary<string, HashSet<EmdDocument>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (chunkId, chunk) in chunksByChunkId)
        {
            var frequencies = Tokenizer.TokenizeWithFrequency(chunk.RawContent, false);
            
            _chunkTokensByChunkId.Add(chunkId, frequencies);
            
            var chunkLength = 0;
            foreach (var frequency in frequencies.Values)
            {
                chunkLength += frequency;
            }
            
            totalDocumentLengthTokens += chunkLength;

            foreach (var (term, frequency) in frequencies)
            {
                // Local term frequency:
                if (!_invertedIndexForChunks.TryGetValue(term, out var postings))
                {
                    postings = [];
                    _invertedIndexForChunks.Add(term, postings);
                }

                postings.Add(new ChunkEntry(chunkId, frequency, chunkLength));
                
                // Global document frequency:
                if (!parentDocuments.TryGetValue(term, out var documentSet))
                {
                    documentSet = [];
                    parentDocuments.Add(term, documentSet);
                }
                
                documentSet.Add(chunk.Node.Document);
            }
        }
        
        foreach (var (term, documentSet) in parentDocuments)
        {
            _globalDocumentFrequencies.Add(term, documentSet.Count);
        }

        _averageChunkLengthTokens = TotalChunkCount > 0 
            ? (float)totalDocumentLengthTokens / TotalChunkCount 
            : 0;
    }

    /// <summary>
    ///     Returns the number of chunks containing the given term, or 0 if the term is not in the index.
    /// </summary>
    public int GetChunkFrequency(string term) => _invertedIndexForChunks.TryGetValue(term.ToLowerInvariant(), out var postings) 
        ? postings.Count
        : 0;

    /// <summary>
    ///     Returns the tokens for the given chunk.
    /// </summary>
    public Dictionary<string, int> GetChunkTokenSet(int chunkId) => _chunkTokensByChunkId[chunkId];
}
