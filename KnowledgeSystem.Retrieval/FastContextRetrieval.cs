using System.Diagnostics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.EmdParser.MarkdownTree;
using KnowledgeSystem.Hnsw;
using KnowledgeSystem.Retrieval.Embeddings;
using KnowledgeSystem.Retrieval.Engine;
using KnowledgeSystem.Retrieval.Lexical;
using KnowledgeSystem.Retrieval.Telemetry;
using KnowledgeSystems.Extensions;

// ReSharper disable ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
// ReSharper disable ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Retrieval;

public sealed class FastContextRetrieval
{
    private readonly IEmbeddingService _embeddingService;
    private readonly RagEngine _engine;
    private readonly string _query;
    private readonly int _bootstrapCount;
    private readonly float _parameter;
    private readonly int _bm25Count;
    private readonly FileFilter? _filePathFilter;

    private float[] _embedding = [];
    private bool _preparedForRun;
    
    private float[] _centroid = [];
    private float _coherenceThreshold;
    private bool _firstStepDone;

    /// <summary>
    ///     Set to true when a Step fetches results but none pass the coherence filter, or when the search returns no results at all.
    ///     The caller should stop stepping when true.
    /// </summary>
    public bool IsExhausted { get; private set; }

    /// <summary>
    ///     All vectors fetched by retrieval.
    /// </summary>
    public readonly HashSet<int> VisitedVectors = [];
    
    /// <summary>
    ///     The resulting document trees.
    /// </summary>
    public readonly Dictionary<EmdDocument, ReferencedDocument> ReferencedDocuments = [];
    
    public FastContextRetrieval(IEmbeddingService embeddingService, RagEngine engine, Description description)
    {
        _embeddingService = embeddingService;
        _engine = engine;
        _query = description.Query;
        _bootstrapCount = description.BootstrapCount;
        _parameter = description.Parameter;
        _bm25Count = description.Bm25Results;
        
        // P.S. If we ever use this in some other place, we need to change the error handling.
        if (!string.IsNullOrWhiteSpace(description.FilePathPattern))
        {
            var regex = new Regex(
                description.FilePathPattern, 
                RegexOptions.Compiled | RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(1.0)
            );
            
            _filePathFilter = new FileFilter(regex);
        }
    }

    /// <summary>
    ///     Embeds the query.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task PrepareForRun(CancellationToken cancellationToken = default)
    {
        if (_preparedForRun)
        {
            throw new InvalidOperationException("Already prepared for run!");
        }

        using var activity = RagTelemetry.Rag.StartInternalActivity("Embed");
        
        var results = await _embeddingService.EmbedAsync(_query, cancellationToken);

        _embedding = results.ToArray();
        _preparedForRun = true;

        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    private bool FileFilterPredicate(int vector)
    {
        var filter = _filePathFilter;
       
        if (filter == null)
        {
            return true;
        }

        if (!filter.MemoizedResults.TryGetValue(vector, out var result))
        {
            var chunk = _engine.ChunkByHnswId[vector];
            var path = chunk.Node.Document.Path;
            result = filter.Matcher.IsMatch(path);
            filter.MemoizedResults.Add(vector, result);
        }

        return result;
    }
    
    private bool SemanticSearchPredicate(int vector)
    {
        return !VisitedVectors.Contains(vector) && FileFilterPredicate(vector);
    }

    /// <summary>
    ///     Gets the fixed number of results using BM25 ranking.
    ///     Does not prevent vector search from finding the same results. When that happens, the score will be recorded for RRF.
    /// </summary>
    private void Bm25()
    {
        using var activity = RagTelemetry.Rag.StartInternalActivity("BM25");
        
        var bm25Results = _engine.LexicalIndex.SearchBm25(_query);
        var passedCount = 0;

        for (var index = 0; index < bm25Results.Length && passedCount < _bm25Count; index++)
        {
            var bm25Result = bm25Results[index];

            if (!FileFilterPredicate(bm25Result.HnswId))
            {
                continue;
            }
            
            ++passedCount;
                
            var chunk = _engine.GetChunkByHnswId(bm25Result.HnswId);
            
            if (!ReferencedDocuments.TryGetValue(chunk.Node.Document, out var referencedDocument))
            {
                referencedDocument = new ReferencedDocument(chunk.Node.Document);
                ReferencedDocuments.Add(chunk.Node.Document, referencedDocument);
            }

            referencedDocument.References.Add(bm25Result.HnswId, new ReferencedDocument.Reference
            {
                Chunk = chunk,
                HnswId = bm25Result.HnswId,
                HasBm25 = true,
                Bm25Score = bm25Result.Score
            });
        }

        activity?.SetTag("passed_count", passedCount);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }
    
    /// <summary>
    ///     Fetches more results for the query and updates the bounding tree of the results.
    ///     Results that fail the semantic coherence filter are silently dropped.
    /// </summary>
    /// <param name="count">The number of top results to fetch. If not bootstrapped, it will first fetch the number of results needed for bootstrapping.</param>
    /// <returns>The total character count of all bounding trees so far.</returns>
    public int Step(int count)
    {
        if (!_preparedForRun)
        {
            throw new InvalidOperationException("Not prepared for step!");
        }
     
        using var activity = RagTelemetry.Rag.StartInternalActivity("Step");
        
        var fetchCount = _firstStepDone 
            ? count 
            : _bootstrapCount;

        activity?.SetTag("fetch_count", fetchCount);

        VectorSearchResult[] vectorResults;
        using (RagTelemetry.Rag.StartInternalActivity("VectorSearch"))
        {
            vectorResults = _engine.Search(
                _embedding, 
                fetchCount,
                efSearch: 1000,
                predicate: SemanticSearchPredicate
            );
        }

        if (vectorResults.Length == 0)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            IsExhausted = true;
            return UpdateTreesAndGetChars();
        }

        if (!_firstStepDone)
        {
            Bm25();
            Bootstrap(vectorResults);
            
            _firstStepDone = true;
        }

        // Apply coherence filter and add accepted results:
        var accepted = 0;
        for (var resultIndex = 0; resultIndex < vectorResults.Length; resultIndex++)
        {
            var vectorSearchResult = vectorResults[resultIndex];
            VisitedVectors.Add(vectorSearchResult.Index);

            var vector = _engine.Hnsw.Vectors[vectorSearchResult.Index]!.VectorView;
            var coherence = VectorObjective.AdjustedCosineSimilarity(vector, _centroid);

            if (coherence > _coherenceThreshold)
            {
                continue;
            }

            accepted++;
            var chunk = _engine.GetChunkByHnswId(vectorSearchResult.Index);

            if (!ReferencedDocuments.TryGetValue(chunk.Node.Document, out var referencedDocument))
            {
                referencedDocument = new ReferencedDocument(chunk.Node.Document);
                ReferencedDocuments.Add(chunk.Node.Document, referencedDocument);
            }
            
            // If BM25 already added this chunk, merge the vector score:
            if (referencedDocument.References.TryGetValue(vectorSearchResult.Index, out var existing))
            {
                existing.HasVectorResult = true;
                existing.VectorScore = vectorSearchResult.Score;
                referencedDocument.References[vectorSearchResult.Index] = existing;
            }
            else
            {
                referencedDocument.References.Add(vectorSearchResult.Index, new ReferencedDocument.Reference
                {
                    Chunk = chunk,
                    HnswId = vectorSearchResult.Index,
                    HasVectorResult = true,
                    VectorScore = vectorSearchResult.Score
                });
            }

            var rawNode = chunk.Node.RawNode;
            
            Debug.Assert(rawNode.NodeType != MarkdownNode.Type.Document);

            if (rawNode.NodeType.IsHeading())
            {
                continue;
            }
            
            if (rawNode.Parent != null && rawNode.Parent.NodeType != MarkdownNode.Type.Document)
            {
                referencedDocument.LogicalParents.Add(rawNode.Parent);
            }
        }

        if (_firstStepDone && accepted == 0)
        {
            IsExhausted = true;
        }
        
        activity?.SetTag("accepted", accepted);
        activity?.SetTag("exhausted", IsExhausted);
        activity?.SetStatus(ActivityStatusCode.Ok);
        
        return UpdateTreesAndGetChars();
    }

    /// <summary>
    ///     Computes Reciprocal Rank Fusion scores for all references across BM25 and vector results, then recomputes bounding trees with the fused scores.
    ///     Should be called after all <see cref="Step"/> calls are done (either exhausted or caller chose to stop).
    /// </summary>
    public int FuseScoresAndFinish(int k = 60)
    {
        // Collect all references globally, partitioned by search method:
        var vectorRefs = new List<ReferencedDocument.Reference>();
        var bm25Refs = new List<ReferencedDocument.Reference>();

        foreach (var doc in ReferencedDocuments.Values)
        {
            foreach (var reference in doc.References.Values)
            {
                if (reference.HasVectorResult)
                {
                    vectorRefs.Add(reference);
                }

                if (reference.HasBm25)
                {
                    bm25Refs.Add(reference);
                }
            }
        }

        // Sort vector by score ascending (see VectorObjective), BM25 by score descending:
        vectorRefs.Sort((a, b) => a.VectorScore.CompareTo(b.VectorScore));
        bm25Refs.Sort((a, b) => b.Bm25Score.CompareTo(a.Bm25Score));

        // Build rank maps:
        var vectorRanks = new Dictionary<int, int>(vectorRefs.Count);
        for (var i = 0; i < vectorRefs.Count; i++)
        {
            vectorRanks[vectorRefs[i].HnswId] = i + 1;
        }

        var bm25Ranks = new Dictionary<int, int>(bm25Refs.Count);
        for (var i = 0; i < bm25Refs.Count; i++)
        {
            bm25Ranks[bm25Refs[i].HnswId] = i + 1;
        }

        // Compute RRF:
        foreach (var document in ReferencedDocuments.Values)
        {
            var keys = document.References.Keys.ToArray();
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                var reference = document.References[key];
                var rrf = 0.0;

                if (vectorRanks.TryGetValue(key, out var vectorRank))
                {
                    rrf += 1.0 / (k + vectorRank);
                }

                if (bm25Ranks.TryGetValue(key, out var bm25Rank))
                {
                    rrf += 1.0 / (k + bm25Rank);
                }

                reference.FusedScore = rrf;
                document.References[key] = reference;
            }
        }

        // Recompute bounding trees with fused scores
        var totalChars = 0;
        foreach (var document in ReferencedDocuments.Values)
        {
            document.ComputeBoundingTrees(out var documentChars);
            
            totalChars += documentChars;
        }
        
        return totalChars;
    }

    /// <summary>
    ///     Detects query tokens that are statistically underrepresented in the results using a Poisson CDF test.
    ///     Returns gap tokens with their observed count in results and total count in the corpus.
    /// </summary>
    public List<GapToken> ExtractGapTokens(float significanceLevel = 0.05f)
    {
        var queryTokens = Tokenizer.TokenizeWithFrequency(_query, false);
        
        if (queryTokens.Count == 0)
        {
            return [];
        }

        // Collect all referenced HNSW IDs and their tokens:
        var referencedChunks = new List<(int HnswId, Dictionary<string, int> Tokens)>();
        foreach (var document in ReferencedDocuments.Values)
        {
            foreach (var hnswId in document.References.Keys)
            {
                referencedChunks.Add((hnswId, _engine.LexicalIndex.GetChunkTokenSet(hnswId)));
            }
        }

        // For each query token, count how many referenced chunks contain it
        var tokenCounts = new List<(string Token, int InResults, int InCorpus)>(queryTokens.Count);
        foreach (var token in queryTokens.Keys)
        {
            var inResults = 0;
            foreach (var (_, tokens) in referencedChunks)
            {
                if (tokens.ContainsKey(token))
                {
                    inResults++;
                }
            }
            
            var inCorpus = _engine.LexicalIndex.GetChunkFrequency(token);
            tokenCounts.Add((token, inResults, inCorpus));
        }

        // Mean of observed frequencies across all query tokens:
        var lambda = tokenCounts.Count > 0
            ? tokenCounts.Average(t => (double)t.InResults)
            : 0;

        // Poisson CDF test per token to find out if the observed count is significantly below expected:
        var gaps = new List<GapToken>(4);
        foreach (var (token, inResults, inCorpus) in tokenCounts)
        {
            if (lambda <= 0)
            {
                continue;
            }
            
            var pValue = PoissonCdf(inResults, lambda);
            if (pValue < significanceLevel)
            {
                gaps.Add(new GapToken(token, inResults, inCorpus, pValue));
            }
        }

        return gaps;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double PoissonCdf(int k, double lambda)
    {
        var sum = 0.0;
        var term = Math.Exp(-lambda);
        for (var i = 0; i <= k; i++)
        {
            sum += term;
            term *= lambda / (i + 1);
        }

        return sum;
    }

    /// <summary>
    ///     A query token that is statistically underrepresented in the search results.
    /// </summary>
    public readonly struct GapToken(string token, int inResults, int inCorpus, double pValue)
    {
        /// <summary>
        ///     The gap token.
        /// </summary>
        public readonly string Token = token;

        /// <summary>
        ///     How many referenced chunks contain this token.
        /// </summary>
        public readonly int InResults = inResults;

        /// <summary>
        ///     How many chunks in the entire corpus contain this token.
        /// </summary>
        public readonly int InCorpus = inCorpus;

        /// <summary>
        ///     The Poisson CDF p-value. Lower means more absent.
        /// </summary>
        public readonly double PValue = pValue;
    }

    /// <summary>
    ///     Computes a weighted centroid using the first (best) <see cref="_bootstrapCount"/> results and sets the <see cref="_coherenceThreshold"/>. 
    /// </summary>
    private void Bootstrap(ReadOnlySpan<VectorSearchResult> results)
    {
        var dimension = _engine.Hnsw.Dimension;
        var centroid = new float[dimension];
        var weightSum = 0.0f;

        var vectors = _engine.Hnsw.Vectors;

        for (var resultIndex = 0; resultIndex < results.Length; resultIndex++)
        {
            var result = results[resultIndex];
            var weight = 1.0f / (result.Score + 0.01f);

            weightSum += weight;
            
            var vector = vectors[result.Index]!.VectorView;
            for (var i = 0; i < dimension; i++)
            {
                centroid[i] += vector[i] * weight;
            }
        }
        
        var recipWeightSum = 1.0f / weightSum;
        for (var i = 0; i < dimension; i++)
        {
            centroid[i] *= recipWeightSum;
        }
        
        // Normalize:
        var recipNorm = 1.0f / MathF.Sqrt(TensorPrimitives.Dot(centroid, centroid));
        for (var i = 0; i < dimension; i++)
        {
            centroid[i] *= recipNorm;
        }

        var coherenceScores = new float[results.Length];
        for (var resultIndex = 0; resultIndex < results.Length; resultIndex++)
        {
            var vector = vectors[results[resultIndex].Index]!.VectorView;
            coherenceScores[resultIndex] = VectorObjective.AdjustedCosineSimilarity(vector, centroid);
        }
        
        var scoreCount = coherenceScores.Length; 
        var sortedCoherenceScores = new float[scoreCount];
        coherenceScores.AsSpan().CopyTo(sortedCoherenceScores);
        Array.Sort(sortedCoherenceScores);

        var mid = scoreCount / 2;
        var medianCoherenceScore = scoreCount % 2 != 0 
            ? sortedCoherenceScores[mid]
            : (sortedCoherenceScores[mid - 1] + sortedCoherenceScores[mid]) / 2f;

        var deviations = new float[scoreCount];
        for (var i = 0; i < scoreCount; i++)
        {
            deviations[i] = MathF.Abs(sortedCoherenceScores[i] - medianCoherenceScore);
        }
        
        Array.Sort(deviations);

        var mad = scoreCount % 2 != 0
            ? deviations[mid]
            : (deviations[mid - 1] + deviations[mid]) / 2f;
        
        _centroid = centroid;
        _coherenceThreshold = medianCoherenceScore + _parameter * mad;
    }

    private int UpdateTreesAndGetChars()
    {
        var characterCount = 0;
        foreach (var referencedDocument in ReferencedDocuments.Values)
        {
            referencedDocument.ComputeBoundingTrees(out var documentCharacterCount);
            characterCount += documentCharacterCount;
        }
        
        return characterCount;
    }

    /// <summary>
    ///     Configuration for a <see cref="FastContextRetrieval"/> run (on a single query).
    /// </summary>
    public sealed class Description
    {
        /// <summary>
        ///     The query text to search for.
        /// </summary>
        public required string Query { get; init; }

        /// <summary>
        ///     Number of top results accepted unconditionally to build the centroid.
        ///     Lower values produce a tighter centroid (which means a higher precision, but lower recall).
        ///     Higher values capture more topic breadth but risk contamination from off-topic results.
        ///     Suggested range: 20-30.
        /// </summary>
        public int BootstrapCount { get; init; } = 30;

        /// <summary>
        ///     Multiplier for the <bold>Median Absolute Deviation</bold> when computing the coherence threshold.
        ///     <c>Threshold = Median + Parameter × MAD</c>.
        ///     Lower values are more aggressive (they reject more), and higher values are more permissive.
        ///     Suggested range: [5, 8].
        /// </summary>
        public float Parameter { get; init; } = 5;
        
        /// <summary>
        ///     Regex filter applied to the file paths.
        /// </summary>
        public string? FilePathPattern { get; init; }

        /// <summary>
        ///     The fixed number of BM25 results to pull.
        /// </summary>
        public int Bm25Results { get; init; } = 30;
    }

    private sealed class FileFilter(Regex matcher)
    {
        public readonly Regex Matcher = matcher;

        public readonly Dictionary<int, bool> MemoizedResults = [];
    }
    
    public sealed class ReferencedDocument(EmdDocument document)
    {
        public readonly EmdDocument Document = document;
        
        /// <summary>
        ///     All the search results found.
        ///     Populated by <see cref="Step"/>.
        /// </summary>
        public readonly Dictionary<int, Reference> References = [];
        
        /// <summary>
        ///     Nodes one level higher than the found nodes.
        ///     These will usually be headings.
        ///     Populated by <see cref="Step"/>.
        /// </summary>
        public readonly HashSet<MarkdownNode> LogicalParents = [];
        
        private readonly Dictionary<MarkdownNode, BoundingTree> _boundingTreesByRoot = [];
        private readonly List<BoundingTree> _boundingTreesSorted = [];
        private readonly Stack<Reference> _referencesForBuild = [];
        
        /// <summary>
        ///     Gets the bounding trees computed by <see cref="ComputeBoundingTrees"/>, sorted by their score.
        /// </summary>
        public IReadOnlyList<BoundingTree> BoundingTreesSorted => _boundingTreesSorted;

        /// <summary>
        ///     The average score across all fetched trees.
        /// </summary>
        public double AverageScore => _boundingTreesSorted.Count == 0
            ? 0.0
            : _boundingTreesSorted.Sum(x => x.AverageScore) / _boundingTreesSorted.Count; 
        
        /// <summary>
        ///     Computes all bounding trees and stores them in <see cref="BoundingTreesSorted"/>.
        /// </summary>
        /// <param name="characterCount">The total number of characters found.</param>
        /// <exception cref="Exception"></exception>
        public void ComputeBoundingTrees(out int characterCount)
        {
            _boundingTreesByRoot.Clear();
            _boundingTreesSorted.Clear();

            foreach (var reference in References.Values)
            {
                _referencesForBuild.Push(reference);
            }

            // For each search result, we find the top parent that is part of the added logical parents.
            // This will merge all the results we found in disjoint trees.
            while (_referencesForBuild.Count > 0)
            {
                var front = _referencesForBuild.Pop();
                var currentNode = front.Chunk.Node.RawNode;
                
                // Keeps the highest logical parent found upstream:
                MarkdownNode? topParent = null;
                
                while (currentNode != null)
                {
                    if (LogicalParents.Contains(currentNode))
                    {
                        // Found a potential root node for the tree containing this node.
                        // Note that this ordering allows a reference to be its own parent.
                        topParent = currentNode;
                    }

                    currentNode = currentNode.Parent;
                }

                if (topParent == null)
                {
                    // Possible when the top parent would be the document.
                    topParent = front.Chunk.Node.RawNode;
                }

                if (!_boundingTreesByRoot.TryGetValue(topParent, out var boundingTree))
                {
                    boundingTree = new BoundingTree(topParent);
                    _boundingTreesByRoot.Add(topParent, boundingTree);
                }

                boundingTree.ReferenceCount++;
                boundingTree.ScoreSum += front.FusedScore;
            }

            characterCount = 0;
            
            // Now, we can populate the list and sort it:
            foreach (var tree in _boundingTreesByRoot.Values)
            {
                _boundingTreesSorted.Add(tree);

                var root = tree.Root;
                characterCount += root.EndOffset - root.StartOffset;
            }
            
            _boundingTreesSorted.Sort((a, b) => a.AverageScore.CompareTo(b.AverageScore));
        }
        
        /// <summary>
        ///     Represents a single search result within this document.
        ///     A reference can come from vector search, BM25, or both.
        /// </summary>
        public struct Reference
        {
            public required EmdChunk Chunk { get; init; }

            /// <summary>
            ///     The HNSW index for this chunk.
            /// </summary>
            public required int HnswId { get; init; }

            /// <summary>
            ///     The adjusted cosine similarity.
            ///     Only valid when <see cref="HasVectorResult"/> is true.
            /// </summary>
            public float VectorScore;
            
            /// <summary>
            ///     The BM25 score (higher = better).
            ///     Only valid when <see cref="HasBm25"/> is true.
            /// </summary>
            public float Bm25Score;

            /// <summary>
            ///     The final fused score computed by <see cref="FastContextRetrieval.FuseScoresAndFinish"/>.
            ///     Zero until RRF is called.
            /// </summary>
            public double FusedScore;
            
            /// <summary>
            ///     Whether this reference was also found via vector search.
            /// </summary>
            public bool HasVectorResult;
            
            /// <summary>
            ///     Whether this reference was found via BM25 search.
            /// </summary>
            public bool HasBm25;
        }

        /// <summary>
        ///     Tree within the document. Traversing down, one can find a subset of the nodes found in this document. 
        /// </summary>
        public sealed class BoundingTree(MarkdownNode root)
        {
            /// <summary>
            ///     The uppermost logical parent (usually a heading).
            /// </summary>
            public readonly MarkdownNode Root = root;

            /// <summary>
            ///     The number of references inside this tree.
            /// </summary>
            public int ReferenceCount;

            /// <summary>
            ///     The sum of the vector search scores inside this tree.
            /// </summary>
            public double ScoreSum;
            
            /// <summary>
            ///     The average vector score.
            /// </summary>
            public double AverageScore => ScoreSum / ReferenceCount;
        }
    }
}