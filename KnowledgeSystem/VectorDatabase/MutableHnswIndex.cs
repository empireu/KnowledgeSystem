// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable LoopCanBeConvertedToQuery

namespace KnowledgeSystem.VectorDatabase;

/// <summary>
///     Hierarchical navigable small world data structure (builder) for approximate K nearest vector lookup.
/// </summary>
/// <param name="dimension">The dimension of the stored vectors.</param>
/// <param name="maxConnectionsLane">Maximum number of graph connections in higher layers.</param>
/// <param name="maxConnectionsDense">Maximum number of graph connections in the dense layer.</param>
/// <param name="efConstruction">The exploration factor for construction.</param>
/// <param name="seed">The seed for the random number generator used during construction.</param>
public sealed partial class MutableHnswIndex(int dimension, int maxConnectionsLane, int maxConnectionsDense, int efConstruction = 200, int? seed = null)
{
    public readonly int Dimension = dimension;
    public readonly int MaxConnectionsLane = maxConnectionsLane;
    public readonly int MaxConnectionsDense = maxConnectionsDense;
    public IReadOnlyList<IStoredVector> Vectors => _vectors;

    private readonly double _recipLogMl = 1.0 / Math.Log(maxConnectionsLane);
    private readonly List<StoredVectorImpl> _vectors = new();
    private readonly List<ILayer> _layers = [new DenseLayer(0, maxConnectionsDense)];
    private readonly SearchData _searchData = new();
    private readonly List<ScoredResult> _resultsBuffer = new(200);
    private readonly TrimEdgesData _trimEdgesData = new();
    private readonly Random _random = seed.HasValue ? new Random(seed.Value) : new Random();

    private StoredVectorImpl? _entryPointVector;

    private sealed class SearchData
    {
        public readonly HashSet<int> Visited = new();
        public readonly PriorityQueue<int, float> CandidatesQueue = new();
        public readonly PriorityQueue<int, float> ResultsQueue = new();

        public void Clear()
        {
            Visited.Clear();
            CandidatesQueue.Clear();
            ResultsQueue.Clear();
        }
    }

    private void SearchLayer(SearchData data, StoredVectorImpl query, StoredVectorImpl entry, ILayer layer, int explorationFactor)
    {
        data.Clear();

        var visited = data.Visited;

        // Priority is in score order.
        var candidates = data.CandidatesQueue;
        
        // Priority is in reverse score order.
        var results = data.ResultsQueue;

        var initialScore = VectorObjective.AdjustedCosineSimilarity(query, entry);
        visited.Add(entry.Index);
        candidates.Enqueue(entry.Index, initialScore);
        results.Enqueue(entry.Index, -initialScore);

        while (candidates.TryDequeue(out var currentCandidate, out var currentScore))
        {
            // Bounds for the search:
            // If the best candidate is worse than the current worst result, and the results queue is full, the search ends.
            if (results.Count >= explorationFactor && 
                results.TryPeek(out _, out var inverseWorstScore) && // Always passes. Peek doesn't give the value
                currentScore > -inverseWorstScore)
            {
                break;
            }
            
            // Expand the neighbors of the candidate:
            if (layer.TryGetEdges(currentCandidate, out var edges))
            {
                for (var i = 0; i < edges!.Count; i++)
                {
                    var neighbor = edges[i];

                    if (!visited.Add(neighbor))
                    {
                        continue;
                    }

                    var neighborScore = VectorObjective.AdjustedCosineSimilarity(query, _vectors[neighbor]);

                    results.TryPeek(out _, out var currentInverseWorstScore);
                    if (results.Count < explorationFactor || neighborScore < -currentInverseWorstScore)
                    {
                        candidates.Enqueue(neighbor, neighborScore);
                        results.Enqueue(neighbor, -neighborScore);
                        
                        // Discards the worst result:
                        if (results.Count > explorationFactor)
                        {
                            results.Dequeue();
                        }
                    }
                }
            }
        }
    }

    public VectorSearchResult[] Search(float[] query, int k, int efSearch = 200)
    {
        if (query.Length != Dimension)
        {
            throw new ArgumentException("Invalid query vector dimension");
        }

        if (k < 0)
        {
            throw new ArgumentException("K cannot be negative");
        }

        if (_entryPointVector == null || k == 0)
        {
            return Array.Empty<VectorSearchResult>();
        }

        var queryVector = new StoredVectorImpl(-1, query);

        var currentNode = _entryPointVector;
        var currentScore = VectorObjective.AdjustedCosineSimilarity(queryVector, currentNode);
        for (var layerIndex = _layers.Count - 1; layerIndex > 0; layerIndex--)
        {
            while (true)
            {
                if (!_layers[layerIndex].TryGetEdges(currentNode.Index, out var currentNodeEdges))
                {
                    break;
                }

                var minimumChanged = false;

                for (var i = 0; i < currentNodeEdges!.Count; i++)
                {
                    var neighborNode = _vectors[currentNodeEdges[i]];
                    var neighborScore = VectorObjective.AdjustedCosineSimilarity(queryVector, neighborNode);

                    if (neighborScore < currentScore)
                    {
                        currentNode = neighborNode;
                        currentScore = neighborScore;
                        minimumChanged = true;
                    }
                }

                if (!minimumChanged)
                {
                    break;
                }
            }
        }

        SearchLayer(_searchData, queryVector, currentNode, _layers[0], Math.Max(k, efSearch));

        var queue = _searchData.ResultsQueue;
        var count = Math.Min(k, queue.Count);
        var results = new VectorSearchResult[count];
        
        var total = queue.Count;
        var skip = total - count;
        while (skip > 0 && queue.TryDequeue(out _, out _))
        {
            skip--;
        }

        for (var i = count - 1; i >= 0; i--)
        {
            queue.TryDequeue(out var element, out var inverseScore);
            results[i] = new VectorSearchResult(element, -inverseScore);
        }

        return results;
    }

    private sealed class StoredVectorImpl(int index, float[] storage) : IStoredVector
    {
        public int Index { get; } = index;
        
        public readonly float[] Storage = storage;

        public ReadOnlySpan<float> StorageView =>  Storage.AsSpan();

        public void Load(float[] data)
        {
            if (data.Length != Storage.Length)
            {
                throw new ArgumentException($"Cannot load data vector of dimension {data.Length} into vector of dimension {Storage.Length}");
            }
            
            data.AsSpan().CopyTo(Storage.AsSpan());
        }
    }
}