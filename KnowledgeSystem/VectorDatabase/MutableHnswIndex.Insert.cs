// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable LoopCanBeConvertedToQuery

namespace KnowledgeSystem.VectorDatabase;

public sealed partial class MutableHnswIndex
{
    /// <summary>
    ///     Allocates a vector and loads in the <see cref="sourceData"/>.
    /// </summary>
    /// <param name="sourceData">A (temporary) vector that matches the <see cref="Dimension"/>.</param>
    /// <returns>A vector allocated at the last index.</returns>
    private StoredVectorImpl AllocateVector(float[] sourceData)
    {
        var storage = _vectorAllocator.Allocate();
        var result = new StoredVectorImpl(_vectors.Count, storage);

        _vectors.Add(result);
        result.Load(sourceData);

        return result;
    }

    /// <summary>
    ///     Rolls a layer using the formula for the layer, adjusted for the .NET Random.
    ///     The output layer is, at most, one higher than the current highest layer.
    /// </summary>
    /// <param name="increasedHeight">If true, the layer rolled above the current highest layer, which creates a new layer.</param>
    /// <returns>The target layer for the inserted node.</returns>
    private ILayer RollLayer(out bool increasedHeight)
    {
        var value = 1.0 - _random.NextDouble();
        var probabilisticIndex = (int)Math.Floor(-Math.Log(value) * _recipLogMl);
        var adjustedIndex = Math.Clamp(probabilisticIndex, 0, Layers.Count);

        if (adjustedIndex < Layers.Count)
        {
            increasedHeight = false;
            return Layers[adjustedIndex];
        }

        increasedHeight = true;

        var layer = new SparseLayer(adjustedIndex, MaxConnectionsLane);
        Layers.Add(layer);
        return layer;
    }
    
    /// <summary>
    ///     Trims the <see cref="edges"/> of a node to the specified maximum count <see cref="maximumEdges"/>, with the special heuristic.
    ///     Also removes reverse edges from evicted neighbors to keep the graph symmetric.
    /// </summary>
    /// <param name="data">Buffer.</param>
    /// <param name="targetNode">The node whose edges are being trimmed.</param>
    /// <param name="edges">The edges of a node that got mutated.</param>
    /// <param name="maximumEdges">The maximum number of edges.</param>
    /// <param name="layer">The layer the edges belong to, used for reverse edge cleanup.</param>
    private void TrimEdges(TrimEdgesData data, StoredVectorImpl targetNode, List<int> edges, int maximumEdges, ILayer layer)
    {
        data.Clear();
        var candidateList = data.Candidates;
        var keptEdges = data.KeptEdges;

        for (var i = 0; i < edges.Count; i++)
        {
            var node = edges[i];
            var score = VectorObjective.AdjustedCosineSimilarity(targetNode, _vectors[node]);
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

            if (!wasKept && layer.TryGetEdges(evictedIndex, out var evictedEdges) && evictedEdges != null)
            {
                evictedEdges.Remove(targetNode.Index);
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

        for (var candidateIndex = 0;
             candidateIndex < candidateList.Count && keptEdges.Count < maximumEdges;
             candidateIndex++)
        {
            var candidate = candidateList[candidateIndex];

            var keep = true;
            var candidateVector = _vectors[candidate.Index];

            // Compares this candidate against every node we are already keeping:
            for (var i = 0; i < keptEdges.Count; i++)
            {
                if (VectorObjective.AdjustedCosineSimilarity(candidateVector, _vectors[keptEdges[i].Index]) <
                    candidate.Score)
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

        var vector = AllocateVector(data);

        if (_entryPointVector == null)
        {
            _entryPointVector = vector;

            return vector;
        }

        var targetLayer = RollLayer(out var increasedHeight);

        // Finds the closest vector to the inserted one, based on the edges from the layer just above the target layer.
        var currentNode = _entryPointVector!;
        var currentScore = VectorObjective.AdjustedCosineSimilarity(vector, currentNode);
        var currentStructureHeight = increasedHeight ? targetLayer.Index - 1 : Layers.Count - 1;
        for (var layerIndex = currentStructureHeight; layerIndex > targetLayer.Index; layerIndex--)
        {
            // Greedily searches the current level's graph for the best node.
            // The search should not have cycles since the selection by cost will prevent it. 
            while (true)
            {
                if (!Layers[layerIndex].TryGetEdges(currentNode.Index, out var currentNodeEdges))
                {
                    break;
                }

                var minimumChanged = false;

                for (var i = 0; i < currentNodeEdges!.Count; i++)
                {
                    var neighborNode = _vectors[currentNodeEdges[i]];
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

        var retopologizeStart = Math.Min(targetLayer.Index, currentStructureHeight);
        for (var layerIndex = retopologizeStart; layerIndex >= 0; layerIndex--)
        {
            var layer = Layers[layerIndex];

            SearchLayer(_searchData, vector.StorageView, currentNode, layer, ExplorationFactorConstruction);

            // Results are in reverse order. We will pull them into a buffer and read it backward:
            var queue = _searchData.ResultsQueue;
            while (queue.TryDequeue(out var element, out var inverseScore))
            {
                _resultsBuffer.Add(new ScoredResult(element, -inverseScore));
            }

            var foundBest = _vectors[_resultsBuffer[^1].Index];

            var maxConnections = layerIndex == 0 ? MaxConnectionsDense : MaxConnectionsLane;
            TrimEdges(_trimEdgesData, _resultsBuffer, maxConnections);

            var vectorEdges = layer.GetOrCreateEdges(vector.Index);

            for (var i = 0; i < _resultsBuffer.Count; i++)
            {
                var neighbor = _vectors[_resultsBuffer[i].Index];
                var neighborEdges = layer.GetOrCreateEdges(neighbor.Index);

                vectorEdges.Add(neighbor.Index);
                neighborEdges.Add(vector.Index);

                if (neighborEdges.Count > maxConnections)
                {
                    TrimEdges(_trimEdgesData, neighbor, neighborEdges, maxConnections, layer);
                }
            }

            // Update the current node to the best one found on the layer by the extended search:
            currentNode = foundBest;

            _resultsBuffer.Clear();
        }

        if (increasedHeight)
        {
            _entryPointVector = vector;
        }

        return vector;
    }
    
    private readonly struct ScoredResult(int index, float score)
    {
        public readonly int Index = index;
        public readonly float Score = score;
    }

    private sealed class TrimEdgesData
    {
        public readonly List<TrimEdgesCandidate> Candidates = new();
        public readonly List<TrimEdgesCandidate> KeptEdges = new();

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