using System.Diagnostics;
using System.Numerics.Tensors;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.EmdParser.MarkdownTree;
using KnowledgeSystem.Hnsw;
using KnowledgeSystem.Retrieval.Embeddings;
using KnowledgeSystem.Retrieval.Engine;

// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Retrieval;

public sealed class FastContextRetrieval
{
    private readonly IEmbeddingService _embeddingService;
    private readonly RagEngine _engine;
    private readonly string _query;
    private readonly int _bootstrapCount;
    private readonly float _parameter;

    private float[] _embedding = [];
    private bool _preparedForRun;
    
    private float[] _centroid = [];
    private float _coherenceThreshold;
    private bool _centroidBootstrapped;

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

        var results = await _embeddingService.EmbedAsync(_query, cancellationToken);

        _embedding = results.ToArray();
        _preparedForRun = true;
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
        
        var fetchCount = _centroidBootstrapped ? count : _bootstrapCount;
        var vectorResults = _engine.Search(_embedding, fetchCount, excludedIndices: VisitedVectors);

        if (vectorResults.Length == 0)
        {
            IsExhausted = true;
            return UpdateTreesAndGetChars();
        }

        if (!_centroidBootstrapped)
        {
            Bootstrap(vectorResults);
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
            
            referencedDocument.References.Add(new ReferencedDocument.Reference
            {
                VectorResult = vectorSearchResult,
                Chunk = chunk
            });

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

        if (_centroidBootstrapped && accepted == 0)
        {
            IsExhausted = true;
        }

        return UpdateTreesAndGetChars();
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
        _centroidBootstrapped = true;
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
    }

    public sealed class ReferencedDocument(EmdDocument document)
    {
        public readonly EmdDocument Document = document;
        
        /// <summary>
        ///     All the search results found.
        ///     Populated by <see cref="Step"/>.
        /// </summary>
        public readonly List<Reference> References = [];
        
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

            for (var index = 0; index < References.Count; index++)
            {
                var reference = References[index];
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
                boundingTree.ScoreSum += front.VectorResult.Score;
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
        ///     Represents a single vector search result within this document.
        /// </summary>
        public readonly struct Reference
        {
            public required VectorSearchResult VectorResult { get; init; }
            public required EmdChunk Chunk { get; init; }
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