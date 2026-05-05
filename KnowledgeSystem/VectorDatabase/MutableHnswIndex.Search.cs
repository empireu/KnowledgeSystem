namespace KnowledgeSystem.VectorDatabase;

public sealed partial class MutableHnswIndex
{
    /// <summary>
    ///     Greedy best-first search within a single HNSW layer, expanding up to <see cref="explorationFactor"/> candidates.
    ///     Corresponds to Algorithm 2.
    /// </summary>
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

    /// <summary>
    ///     Searches for the approximate <see cref="k"/> vectrs most similar to <see cref="query"/>.
    /// </summary>
    /// <param name="query">A vector matching the <see cref="Dimension"/>.</param>
    /// <param name="k">The maximum number of vectors to explore.</param>
    /// <param name="efSearch">The exploration factor.</param>
    /// <returns>The found vectors.</returns>
    /// <exception cref="ArgumentException">Thrown if the <see cref="query"/>'s dimension does not match <see cref="Dimension"/>.</exception>
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
        for (var layerIndex = Layers.Count - 1; layerIndex > 0; layerIndex--)
        {
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

        SearchLayer(_searchData, queryVector, currentNode, Layers[0], Math.Max(k, efSearch));

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
}