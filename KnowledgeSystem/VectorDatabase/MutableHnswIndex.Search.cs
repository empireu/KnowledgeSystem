namespace KnowledgeSystem.VectorDatabase;

public sealed partial class MutableHnswIndex
{
    /// <summary>
    ///     Greedy best-first search within a single HNSW layer, expanding up to <see cref="explorationFactor"/> candidates.
    ///     Corresponds to Algorithm 2.
    /// </summary>
    private void SearchLayer(SearchData data, ReadOnlySpan<float> query, StoredVectorImpl entry, int layer, int explorationFactor)
    {
        data.Clear();
        data.EnsureCapacity(VectorsInternal.Count);

        var visited = data.Visited;
        var generation = data.VisitedGeneration;

        // Priority is in score order.
        var candidates = data.CandidatesQueue;

        // Priority is in reverse score order.
        var resultsQueue = data.ResultsQueue;

        var initialScore = VectorObjective.AdjustedCosineSimilarity(query, entry.VectorView);
        visited[entry.Index] = generation;
        candidates.Enqueue(entry.Index, initialScore);
        resultsQueue.Enqueue(entry.Index, -initialScore);

        // ReSharper disable once InlineTemporaryVariable
        var vectors = VectorsInternal;

        while (candidates.TryDequeue(out var currentCandidate, out var currentScore))
        {
            // Bounds for the search:
            // If the best candidate is worse than the current worst result, and the results queue is full, the search ends.
            if (resultsQueue.Count >= explorationFactor &&
                resultsQueue.TryPeek(out _, out var inverseWorstScore) && // Always passes. Peek doesn't give the value
                currentScore > -inverseWorstScore)
            {
                break;
            }

            var edges = vectors[currentCandidate]!.GetEdgesInLayer(layer);

            // Expand the neighbors of the candidate:
            for (var i = 0; i < edges.Count; i++)
            {
                var neighbor = edges[i];

                if (visited[neighbor] == generation)
                {
                    continue;
                }

                visited[neighbor] = generation;

                var neighborScore = VectorObjective.AdjustedCosineSimilarity(query, vectors[neighbor]!.VectorView);

                resultsQueue.TryPeek(out _, out var currentInverseWorstScore);
                if (resultsQueue.Count < explorationFactor || neighborScore < -currentInverseWorstScore)
                {
                    candidates.Enqueue(neighbor, neighborScore);
                    resultsQueue.Enqueue(neighbor, -neighborScore);

                    // Discards the worst result:
                    if (resultsQueue.Count > explorationFactor)
                    {
                        resultsQueue.Dequeue();
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Searches for the approximate <see cref="k"/> vectrs most similar to <see cref="query"/>.
    /// </summary>
    /// <param name="query">A vector matching the <see cref="Dimension"/>.</param>
    /// <param name="k">The maximum number of vectors to explore.</param>
    /// <param name="efSearch">The exploration factor.</param>
    /// <returns>The found vectors.</returns>
    /// <exception cref="ArgumentException">Thrown if the <see cref="query"/>'s dimension does not match <see cref="Dimension"/>.</exception>
    public VectorSearchResult[] Search(ReadOnlySpan<float> query, int k, int efSearch = 200)
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
            return [];
        }
        
        var currentNode = _entryPointVector;
        var currentScore = VectorObjective.AdjustedCosineSimilarity(query, currentNode.VectorView);
        for (var layerIndex = LayerCount - 1; layerIndex > 0; layerIndex--)
        {
            while (true)
            {
                var currentNodeEdges = currentNode.GetEdgesInLayer(layerIndex);
                var minimumChanged = false;

                for (var i = 0; i < currentNodeEdges.Count; i++)
                {
                    var neighborNode = VectorsInternal[currentNodeEdges[i]]!;
                    var neighborScore = VectorObjective.AdjustedCosineSimilarity(query, neighborNode.VectorView);

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

        SearchLayer(_searchData, query, currentNode, 0, Math.Max(k, efSearch));

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
    
    private sealed class SearchData
    {
        public int[] Visited = [];
        public int VisitedGeneration = 1;
        public readonly PriorityQueue<int, float> CandidatesQueue = new();
        public readonly PriorityQueue<int, float> ResultsQueue = new();

        public void Clear()
        {
            VisitedGeneration++;
            CandidatesQueue.Clear();
            ResultsQueue.Clear();
        }

        public void EnsureCapacity(int capacity)
        {
            if (Visited.Length < capacity)
            {
                Visited = new int[capacity];
                VisitedGeneration = 1;
            }
        }
    }
}