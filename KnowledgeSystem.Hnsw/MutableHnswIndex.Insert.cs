// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable LoopCanBeConvertedToQuery

namespace KnowledgeSystem.Hnsw;

public sealed partial class MutableHnswIndex
{
    /// <summary>
    ///     Rolls a layer using the formula for the layer, adjusted for the .NET Random.
    ///     The output layer is, at most, one higher than the current highest layer.
    /// </summary>
    /// <param name="increasedHeight">If true, the layer rolled above the current highest layer, which creates a new layer.</param>
    /// <returns>The target layer for the inserted node.</returns>
    private int RollLayer(out bool increasedHeight)
    {
        var value = 1.0 - _random.NextDouble();
        var probabilisticIndex = (int)Math.Floor(-Math.Log(value) * _recipLogMl);
        var adjustedIndex = Math.Clamp(probabilisticIndex, 0, LayerCount);

        if (adjustedIndex < LayerCount)
        {
            increasedHeight = false;
        }
        else
        {
            LayerCount++;
            increasedHeight = true;
        }

        return adjustedIndex;
    }

    /// <summary>
    ///     Allocates a vector and loads in the <see cref="sourceData"/>.
    ///     Pre-allocates the dense graph storage and all sparse storages (based on the rolled layer).
    /// </summary>
    /// <param name="sourceData">A (temporary) vector that matches the <see cref="Dimension"/>.</param>
    /// <param name="increasedHeight">If true, the rolled layer is one higher than the current highest layer.</param>
    /// <returns>A vector allocated at the last index.</returns>
    private StoredVectorImpl AllocateVector(float[] sourceData, out bool increasedHeight)
    {
        var vectorStorage = _vectorAllocator.Allocate();
        var targetLayer = RollLayer(out increasedHeight);

        var denseGraph = new EdgeList(MaxConnectionsDense + 1, _denseEdgeStorageAllocator.Allocate());
        denseGraph.Clear();
        
        Allocation<EdgeList>? sparseGraphs = targetLayer > 0 
            ? _layerStorageAllocator.Allocate(targetLayer)
            : null;

        if (sparseGraphs.HasValue)
        {
            var array = sparseGraphs.Value.Block.Span;
            for (var i = 0; i < array.Length; i++)
            {
                var sparseList = new EdgeList(MaxConnectionsLane + 1, _laneEdgeStorageAllocator.Allocate());
                sparseList.Clear();
                array[i] = sparseList;
            }
        }

        var isReusedSlot = _freeSlots.Count > 0;
        var index = isReusedSlot ? _freeSlots.Pop() : VectorsInternal.Count;

        var result = new StoredVectorImpl(index, vectorStorage, denseGraph, sparseGraphs);

        if (isReusedSlot)
        {
            VectorsInternal[index] = result;
        }
        else
        {
            VectorsInternal.Add(result);
        }

        result.Load(sourceData);

        return result;
    }

    /// <summary>
    ///     Trims the edges of the <see cref="targetNode"/> in the specified <see cref="layer"/> to the specified maximum count <see cref="maximumEdges"/>, with the special heuristic.
    ///     Also removes reverse edges from evicted neighbors to keep the graph symmetric.
    /// </summary>
    private void TrimEdges(TrimEdgesData data, StoredVectorImpl targetNode, int layer, int maximumEdges)
    {
        data.Clear();
        
        var candidateList = data.Candidates;
        var keptEdges = data.KeptEdges;
        var edges = targetNode.GetEdgesInLayer(layer);
        
        // ReSharper disable once InlineTemporaryVariable
        var vectors = VectorsInternal;

        for (var i = 0; i < edges.Count; i++)
        {
            var node = edges[i];
            var score = VectorObjective.AdjustedCosineSimilarity(targetNode, vectors[node]!);
            candidateList.Add(new TrimEdgesData.TrimEdgesCandidate(node, score));
        }

        data.SortCandidates();
        ApplyTrimHeuristic(data, maximumEdges);

        // Remove reverse edges for evicted neighbors to keep the graph symmetric:
        for (var i = 0; i < edges.Count; i++)
        {
            var evictedIndex = edges[i];
            var wasKept = false;
            for (var j = 0; j < keptEdges.Count; j++)
            {
                if (keptEdges[j].Index == evictedIndex)
                {
                    wasKept = true;
                    break;
                }
            }

            if (!wasKept)
            {
                if (!vectors[evictedIndex]!.GetEdgesInLayer(layer).Remove(targetNode.Index))
                {
                    throw new Exception("Expected to remove neighbor node");
                }
            }
        }

        edges.Clear();

        for (var index = 0; index < keptEdges.Count; index++)
        {
            edges.Add(keptEdges[index].Index);
        }
    }

    /// <summary>
    ///     Trims the <see cref="scoredResults"/> target neighbors found by exploration to, at most, <see cref="maximumEdges"/>.
    ///     Uses the trim heuristic as well.
    /// </summary>
    /// <param name="data"></param>
    /// <param name="scoredResults"></param>
    /// <param name="maximumEdges"></param>
    private void TrimEdges(TrimEdgesData data, List<ScoredResult> scoredResults, int maximumEdges)
    {
        data.Clear();
        var candidateList = data.Candidates;
        var keptEdges = data.KeptEdges;

        for (var i = 0; i < scoredResults.Count; i++)
        {
            var result = scoredResults[i];
            candidateList.Add(new TrimEdgesData.TrimEdgesCandidate(result.Index, result.Score));
        }

        data.SortCandidates();
        ApplyTrimHeuristic(data, maximumEdges);

        scoredResults.Clear();
        for (var i = 0; i < keptEdges.Count; i++)
        {
            var kept = keptEdges[i];
            scoredResults.Add(new ScoredResult(kept.Index, kept.Score));
        }
    }

    /// <summary>
    ///     Applies the HNSW trim heuristic to select which candidate edges to keep.
    ///     Candidates are processed in ascending order of "distance" (best-first).
    ///     A candidate is kept only if its similarity to every already-kept neighbor is greater than or equal to its similarity to the query point, to keep spatial diversity among retained edges.
    ///     This is done to not break the graph such that it becomes less connected, which would make the recall bad.
    /// </summary>
    /// <param name="data"></param>
    /// <param name="maximumEdges"></param>
    private void ApplyTrimHeuristic(TrimEdgesData data, int maximumEdges)
    {
        var candidateList = data.Candidates;
        var keptEdges = data.KeptEdges;

        for (var candidateIndex = 0; candidateIndex < candidateList.Count && keptEdges.Count < maximumEdges; candidateIndex++)
        {
            var candidate = candidateList[candidateIndex];

            var keep = true;
            var candidateVector = VectorsInternal[candidate.Index]!;

            // Compares this candidate against every node we are already keeping:
            for (var i = 0; i < keptEdges.Count; i++)
            {
                if (VectorObjective.AdjustedCosineSimilarity(candidateVector, VectorsInternal[keptEdges[i].Index]!) < candidate.Score)
                {
                    keep = false;
                    break;
                }
            }

            if (keep)
            {
                keptEdges.Add(candidate);
            }
        }
    }

    /// <summary>
    ///     Inserts a new vector into the database.
    /// </summary>
    /// <param name="data"></param>
    /// <exception cref="ArgumentException"></exception>
    public IStoredVector Insert(float[] data)
    {
        if (data.Length != Dimension)
        {
            throw new ArgumentException("Invalid data vector dimension");
        }

        var vector = AllocateVector(data, out var increasedHeight);

        if (EntryPointVector == null)
        {
            EntryPointVector = vector;

            return vector;
        }
        
        // ReSharper disable once InlineTemporaryVariable
        var vectors = VectorsInternal;

        var targetLayer = vector.TargetLayer;
        
        // Finds the closest vector to the inserted one, based on the edges from the layer just above the target layer.
        var currentNode = EntryPointVector!;
        var currentScore = VectorObjective.AdjustedCosineSimilarity(vector, currentNode);
        var currentStructureHeight = increasedHeight ? targetLayer - 1 : LayerCount - 1;
        for (var layerIndex = currentStructureHeight; layerIndex > targetLayer; layerIndex--)
        {
            // Greedily searches the current level's graph for the best node.
            // The search should not have cycles since the selection by cost will prevent it. 
            while (true)
            {
                var currentNodeEdges = currentNode.GetEdgesInLayer(layerIndex);
                var minimumChanged = false;

                for (var i = 0; i < currentNodeEdges.Count; i++)
                {
                    var neighborNode = vectors[currentNodeEdges[i]]!;
                    var neighborScore = VectorObjective.AdjustedCosineSimilarity(vector, neighborNode);

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

            // On the next iterations, we will expand nodes on the denser layer below.
        }
        
        var searchData = _searchDataPool.Get();

        try
        {
             var retopologizeStart = Math.Min(targetLayer, currentStructureHeight);
             for (var layer = retopologizeStart; layer >= 0; layer--)
             {
                 _resultsBuffer.Clear();

                 SearchLayer(searchData, vector.VectorView, currentNode, layer, ExplorationFactorConstruction, null);

                 // Results are in reverse order. We will pull them into a buffer and read it backward:
                 var resultsQueue = searchData.ResultsQueue;
                 while (resultsQueue.TryDequeue(out var element, out var inverseScore))
                 {
                     _resultsBuffer.Add(new ScoredResult(element, -inverseScore));
                 }

                 var foundBest = vectors[_resultsBuffer[^1].Index]!;

                 var maxConnections = layer == 0 ? MaxConnectionsDense : MaxConnectionsLane;
                 TrimEdges(_trimEdgesData, _resultsBuffer, maxConnections);

                 var vectorEdges = vector.GetEdgesInLayer(layer);

                 for (var i = 0; i < _resultsBuffer.Count; i++)
                 {
                     var neighbor = vectors[_resultsBuffer[i].Index]!;
                     var neighborEdges = neighbor.GetEdgesInLayer(layer);

                     vectorEdges.Add(neighbor.Index);
                     neighborEdges.Add(vector.Index);

                     if (neighborEdges.Count > maxConnections)
                     {
                         TrimEdges(_trimEdgesData, neighbor, layer, maxConnections);
                     }
                 }

                 // Update the current node to the best one found on the layer by the extended search:
                 currentNode = foundBest;
             }

             if (increasedHeight)
             {
                 EntryPointVector = vector;
             }

             return vector;
        }
        finally
        {
            _searchDataPool.Return(searchData);
        }
    }
    
    private readonly struct ScoredResult(int index, float score)
    {
        public readonly int Index = index;
        public readonly float Score = score;
    }

    private sealed class TrimEdgesData
    {
        public readonly List<TrimEdgesCandidate> Candidates = [];
        public readonly List<TrimEdgesCandidate> KeptEdges = [];

        public void SortCandidates()
        {
            Candidates.Sort(CompareCandidates);
        }

        private static int CompareCandidates(TrimEdgesCandidate a, TrimEdgesCandidate b)
        {
            return a.Score.CompareTo(b.Score);
        }

        public void Clear()
        {
            Candidates.Clear();
            KeptEdges.Clear();
        }

        public readonly struct TrimEdgesCandidate(int index, float score)
        {
            public readonly int Index = index;
            public readonly float Score = score;
        }
    }
}