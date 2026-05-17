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
    /// <remarks>Must be called under <see cref="_allocationLock"/>.</remarks>
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
    /// <remarks>Must be called under <see cref="_allocationLock"/>.</remarks>
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
    ///     Removes reverse edges from evicted neighbors to maintain graph symmetry.
    /// </summary>
    /// <remarks>Must be called under <see cref="_graphLock"/>.</remarks>
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

        // Remove reverse edges for evicted neighbors:
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
                vectors[evictedIndex]!.GetEdgesInLayer(layer).Remove(targetNode.Index);
            }
        }

        // Rebuild the target node's edge list with kept edges only:
        edges.Clear();
        for (var index = 0; index < keptEdges.Count; index++)
        {
            edges.Add(keptEdges[index].Index);
        }
    }

    /// <summary>
    ///     Trims the <see cref="scoredResults"/> target neighbors found by exploration to, at most, <see cref="maximumEdges"/>.
    ///     Uses the trim heuristic as well.
    ///     This overload operates on a local results list and does not touch the graph.
    /// </summary>
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
    ///     Thread-safe: may be called concurrently from multiple threads.
    ///     The search phase runs without locks (parallel with other threads).
    ///     The edge wiring phase is serialized under a global graph lock to maintain symmetric edges.
    /// </summary>
    /// <param name="data"></param>
    /// <exception cref="ArgumentException"></exception>
    public IStoredVector Insert(float[] data)
    {
        if (data.Length != Dimension)
        {
            throw new ArgumentException("Invalid data vector dimension");
        }

        StoredVectorImpl vector;
        bool increasedHeight;
        StoredVectorImpl? snapshotEntryPoint;
        int currentStructureHeight;

        // Allocation phase: protected by a global lock.
        // This is brief (no search happens here), protecting the allocators, RNG, free slots, and VectorsInternal growth.
        lock (_allocationLock)
        {
            vector = AllocateVector(data, out increasedHeight);

            // Snapshot the entry point while still under lock. Use its actual TargetLayer
            // rather than LayerCount, because a concurrent AllocateVector may have
            // incremented LayerCount (via RollLayer) without yet updating EntryPointVector:
            snapshotEntryPoint = EntryPointVector;

            if (snapshotEntryPoint == null)
            {
                // Publish under _graphLock so Search's _graphLock read has a happens-before edge:
                lock (_graphLock)
                {
                    EntryPointVector = vector;
                }

                return vector;
            }

            currentStructureHeight = snapshotEntryPoint.TargetLayer;
        }

        // ReSharper disable once InlineTemporaryVariable
        var vectors = VectorsInternal;

        var targetLayer = vector.TargetLayer;

        var searchData = _searchDataPool.Get();
        var insertCtx = _insertContextPool.Get();

        try
        {
            // Ensure scratch buffers are sized for edge snapshots during greedy descent:
            searchData.EnsureScratchCapacity(Math.Max(MaxConnectionsDense, MaxConnectionsLane) + 1);

            // Upper-layer greedy descent. Snapshot edges to avoid reading
            // partially-mutated data while another thread trims under _graphLock:
            var currentNode = snapshotEntryPoint;
            var currentScore = VectorObjective.AdjustedCosineSimilarity(vector, currentNode);
            for (var layerIndex = currentStructureHeight; layerIndex > targetLayer; layerIndex--)
            {
                // Greedily searches the current level's graph for the best node.
                // The search should not have cycles since the selection by cost will prevent it. 
                while (true)
                {
                    var currentNodeEdges = currentNode.GetEdgesInLayer(layerIndex);
                    var edgeCount = currentNodeEdges.Count;
                    searchData.EnsureScratchCapacity(edgeCount);
                    var edgeSnapshot = searchData.EdgeScratch.AsSpan(0, edgeCount);
                    for (var e = 0; e < edgeCount; e++)
                    {
                        edgeSnapshot[e] = currentNodeEdges[e];
                    }

                    var minimumChanged = false;

                    for (var i = 0; i < edgeSnapshot.Length; i++)
                    {
                        var neighborNode = vectors[edgeSnapshot[i]]!;
                        var neighborScore = VectorObjective.AdjustedCosineSimilarity(vector, neighborNode);

                        if (neighborScore < currentScore && neighborNode.TargetLayer >= layerIndex)
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

            var retopologizeStart = Math.Min(targetLayer, currentStructureHeight);
            for (var layer = retopologizeStart; layer >= 0; layer--)
            {
                insertCtx.ResultsBuffer.Clear();

                // SearchLayer is read-only, so no locks needed.
                // This is the expensive part and runs in parallel with other threads' SearchLayer calls:
                SearchLayer(searchData, vector.VectorView, currentNode, layer, ExplorationFactorConstruction, null);

                // Results are in reverse order. We will pull them into a buffer and read it backward:
                var resultsQueue = searchData.ResultsQueue;
                while (resultsQueue.TryDequeue(out var element, out var inverseScore))
                {
                    insertCtx.ResultsBuffer.Add(new ScoredResult(element, -inverseScore));
                }

                var foundBest = vectors[insertCtx.ResultsBuffer[^1].Index]!;

                var maxConnections = layer == 0 ? MaxConnectionsDense : MaxConnectionsLane;

                // Trim the results list (pure computation on local data, no graph mutation):
                TrimEdges(insertCtx.TrimEdgesData, insertCtx.ResultsBuffer, maxConnections);

                // Edge wiring: serialized under the graph lock.
                // This is the cheap part (just array mutations + occasional small trim).
                // Serializing this maintains symmetric edges trivially:
                lock (_graphLock)
                {
                    for (var i = 0; i < insertCtx.ResultsBuffer.Count; i++)
                    {
                        var neighbor = vectors[insertCtx.ResultsBuffer[i].Index]!;

                        // Skip stale neighbors that don't exist at this layer.
                        // Can happen when SearchLayer snapshots a concurrent TrimEdges:
                        if (layer > 0 && neighbor.TargetLayer < layer)
                        {
                            continue;
                        }

                        // Our vector's edge list may already have been filled by reverse edges from other threads' wiring (which also run under this lock, but in prior turns).
                        // If so, trim first to make room:
                        var vectorEdges = vector.GetEdgesInLayer(layer);
                        if (!vectorEdges.TryAdd(neighbor.Index))
                        {
                            TrimEdges(insertCtx.TrimEdgesData, vector, layer, maxConnections);
                            vector.GetEdgesInLayer(layer).Add(neighbor.Index);
                        }

                        var neighborEdges = neighbor.GetEdgesInLayer(layer);
                        if (!neighborEdges.TryAdd(vector.Index))
                        {
                            TrimEdges(insertCtx.TrimEdgesData, neighbor, layer, maxConnections);
                            neighbor.GetEdgesInLayer(layer).Add(vector.Index);
                        }
                        else if (neighborEdges.Count > maxConnections)
                        {
                            TrimEdges(insertCtx.TrimEdgesData, neighbor, layer, maxConnections);
                        }
                    }
                }

                // Update the current node to the best one found on the layer by the extended search:
                currentNode = foundBest;
            }

            if (increasedHeight)
            {
                lock (_graphLock)
                {
                    // Only set entry point if our layer is strictly higher than the current one.
                    // Another thread may have already inserted a vector with a higher target layer.
                    if (EntryPointVector == null || vector.TargetLayer > EntryPointVector.TargetLayer)
                    {
                        EntryPointVector = vector;
                    }
                }
            }

            return vector;
        }
        finally
        {
            _searchDataPool.Return(searchData);
            _insertContextPool.Return(insertCtx);
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