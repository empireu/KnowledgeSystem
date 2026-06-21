using KnowledgeSystem.Lexical;

// ReSharper disable ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Plugins.CodeMemory.Memory;

public sealed class MemoryLexicalIndex
{
    private const float K1 = 1.2f;
    private const float B = 0.75f;

    private readonly Dictionary<string, List<Entry>> _invertedIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _globalDocumentFrequencies = new(StringComparer.OrdinalIgnoreCase);
    private float _averageLengthTokens;

    public int TotalMemoryCount { get; private set; }

    private readonly struct Entry(int memoryId, int termFrequency, int length)
    {
        public readonly int MemoryId = memoryId;
        public readonly int TermFrequency = termFrequency;
        public readonly int Length = length;
    }

    public void Build(IEnumerable<(int Id, string Summary)> memories)
    {
        _invertedIndex.Clear();
        _globalDocumentFrequencies.Clear();

        var totalLength = 0;
        var parentDocuments = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        TotalMemoryCount = 0;

        foreach (var (id, summary) in memories)
        {
            TotalMemoryCount++;

            var frequencies = Tokenizer.TokenizeWithFrequency(summary, false);
            var docLength = 0;
            foreach (var f in frequencies.Values)
            {
                docLength += f;
            }
            totalLength += docLength;

            foreach (var (term, frequency) in frequencies)
            {
                if (!_invertedIndex.TryGetValue(term, out var postings))
                {
                    postings = [];
                    _invertedIndex.Add(term, postings);
                }

                postings.Add(new Entry(id, frequency, docLength));

                if (!parentDocuments.TryGetValue(term, out var docSet))
                {
                    docSet = [];
                    parentDocuments.Add(term, docSet);
                }

                docSet.Add(id);
            }
        }

        foreach (var (term, docSet) in parentDocuments)
        {
            _globalDocumentFrequencies.Add(term, docSet.Count);
        }

        _averageLengthTokens = TotalMemoryCount > 0 ? (float)totalLength / TotalMemoryCount : 0;
    }

    public void Add(int id, string summary)
    {
        var frequencies = Tokenizer.TokenizeWithFrequency(summary, false);
        var docLength = 0;
        foreach (var f in frequencies.Values)
        {
            docLength += f;
        }

        var oldTotal = _averageLengthTokens * TotalMemoryCount;
        TotalMemoryCount++;
        _averageLengthTokens = TotalMemoryCount > 0 ? (float)(oldTotal + docLength) / TotalMemoryCount : 0;

        foreach (var (term, frequency) in frequencies)
        {
            if (!_invertedIndex.TryGetValue(term, out var postings))
            {
                postings = [];
                _invertedIndex.Add(term, postings);
            }

            postings.Add(new Entry(id, frequency, docLength));

            _globalDocumentFrequencies.TryGetValue(term, out var df);
            _globalDocumentFrequencies[term] = df + 1;
        }
    }

    public void Remove(int id, string summary)
    {
        var frequencies = Tokenizer.TokenizeWithFrequency(summary, false);
        var documentLength = 0;
        
        foreach (var frequency in frequencies.Values)
        {
            documentLength += frequency;
        }

        var oldTotal = _averageLengthTokens * TotalMemoryCount;
        TotalMemoryCount--;
        
        _averageLengthTokens = TotalMemoryCount > 0 
            ? (oldTotal - documentLength) / TotalMemoryCount 
            : 0;

        foreach (var (term, _) in frequencies)
        {
            if (!_invertedIndex.TryGetValue(term, out var postings))
            {
                continue;
            }

            postings.RemoveAll(e => e.MemoryId == id);

            if (postings.Count == 0)
            {
                _invertedIndex.Remove(term);
            }

            if (_globalDocumentFrequencies.TryGetValue(term, out var df))
            {
                if (df <= 1)
                {
                    _globalDocumentFrequencies.Remove(term);
                }
                else
                {
                    _globalDocumentFrequencies[term] = df - 1;
                }
            }
        }
    }

    public Bm25Result[] SearchBm25(string query, int topN = int.MaxValue)
    {
        var frequencyTable = Tokenizer.TokenizeWithFrequency(query, false);

        if (frequencyTable.Count == 0 || TotalMemoryCount == 0 || _averageLengthTokens == 0)
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

            var globalDf = _globalDocumentFrequencies.TryGetValue(term, out var df) ? df : 1;
            var idf = MathF.Log((TotalMemoryCount - globalDf + 0.5f) / (globalDf + 0.5f) + 1.0f);

            for (var i = 0; i < postings.Count; i++)
            {
                var posting = postings[i];
                var tfNorm = posting.TermFrequency * (K1 + 1.0f) / (posting.TermFrequency + K1 * (1.0f - B + B * posting.Length / _averageLengthTokens));
                var score = idf * tfNorm * frequency;

                if (!scores.TryGetValue(posting.MemoryId, out var existingScore))
                {
                    scores[posting.MemoryId] = score;
                }
                else
                {
                    scores[posting.MemoryId] = existingScore + score;
                }
            }
        }

        var count = Math.Min(topN, scores.Count);
        if (count == 0)
        {
            return [];
        }

        // Sort by score descending and take topN:
        var sorted = scores
            .OrderByDescending(x => x.Value)
            .Take(count);

        var results = new Bm25Result[count];
        var index = 0;
        foreach (var (memoryId, score) in sorted)
        {
            results[index++] = new Bm25Result(memoryId, score);
        }

        return results;
    }
}