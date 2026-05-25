//#define TwoHop

using System.Diagnostics;
using System.Runtime.CompilerServices;

// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Vector.Hnsw;

public sealed partial class MutableHnswIndex
{
    /// <summary>
    ///     Tracks what <see cref="SearchLayer"/> does, to investigate potential overhead.
    /// </summary>
    public struct SearchLayerTrackInfo
    {
        /// <summary>
        ///     The number of array resize operations across all scratch buffers.
        /// </summary>
        public int ResizeOperations;
        
        /// <summary>
        ///     The number of vector distance calculations done.
        /// </summary>
        public int DistanceCalculations;
        
        /// <summary>
        ///     The number of candidates enqueued (the candidate queue).
        /// </summary>
        public int CandidateEnqueueOperations;

        /// <summary>
        ///     The number of results enqueued (the result queue).
        /// </summary>
        public int ResultEnqueueOperations;
        
        /// <summary>
        ///     The number of candidates dequeued from the queue.
        /// </summary>
        public int CandidateDequeueOperations;

        /// <summary>
        ///     The number of results dequeued from the queue.
        /// </summary>
        public int ResultDequeueOperations;

        /// <summary>
        ///     Loads all data as tags in the activity.
        /// </summary>
        /// <param name="activity"></param>
        public void SetAsTags(Activity? activity)
        {
            if (activity == null)
            {
                return;
            }
            
            activity.SetTag("resize_operations", ResizeOperations);
            activity.SetTag("distance_calculations", DistanceCalculations);
            activity.SetTag("candidate_enqueue_operations", CandidateEnqueueOperations);
            activity.SetTag("result_enqueue_operations", ResultEnqueueOperations);
            activity.SetTag("candidate_dequeue_operations", CandidateDequeueOperations);
            activity.SetTag("result_dequeue_operations", ResultDequeueOperations);
        }
    }
    
    /// <summary>
    ///     Greedy best-first search within a single HNSW layer, expanding up to <see cref="explorationFactor"/> candidates.
    ///     Corresponds to Algorithm 2.
    /// </summary>
    private SearchLayerTrackInfo SearchLayer(SearchData data, ReadOnlySpan<float> query, StoredVectorImpl entry, int layer, int explorationFactor, Predicate<int>? predicate)
    {
        var track = new SearchLayerTrackInfo();
        
        data.Clear();
        if (data.EnsureCapacity(VectorsInternal.Count))
        {
            track.ResizeOperations++;
        }

        // Priority is in score order.
        var candidates = data.CandidatesQueue;

        // Priority is in reverse score order.
        var resultsQueue = data.ResultsQueue;

        var initialScore = VectorObjective.AdjustedCosineSimilarity(query, entry.VectorView);
        ++track.DistanceCalculations;

        data.MarkVisited(entry.Index, ref track.ResizeOperations);
        candidates.Enqueue(entry.Index, initialScore);
        ++track.CandidateEnqueueOperations;
 
        // Entry point is added to candidates for traversal, but only to results if not excluded:
        if (predicate == null || predicate(entry.Index))
        {
            resultsQueue.Enqueue(entry.Index, -initialScore);
            ++track.ResultEnqueueOperations;
        }

        // ReSharper disable once InlineTemporaryVariable
        var vectors = VectorsInternal;

        // Ensure scratch buffers are large enough for edge snapshots.
        // Max edge count is bounded by EdgeCapacity (MaxConnectionsDense + 1 or MaxConnectionsLane + 1).
        // Use the larger of the two to cover any layer:
        data.EnsureScratchCapacity(Math.Max(MaxConnectionsDense, MaxConnectionsLane) + 1, ref track.ResizeOperations);

        while (candidates.TryDequeue(out var currentCandidate, out var currentScore))
        {
            ++track.CandidateDequeueOperations;
            
            // Bounds for the search:
            // If the best candidate is worse than the current worst result, and the results queue is full, the search ends.
            if (//resultsQueue.Count >= 0 && -> Bad filters could cause this to blow up.
                resultsQueue.TryPeek(out _, out var inverseWorstScore) && // Always passes. Peek doesn't give the value
                currentScore > -inverseWorstScore)
            {
                break;
            }
            
            if (layer > 0 && vectors[currentCandidate]!.TargetLayer < layer)
            {
                continue;
            }

            var edges = vectors[currentCandidate]!.GetEdgesInLayer(layer);
            var edgeCount = edges.Count;
            
            // P.S. Is this call redundant?
            data.EnsureScratchCapacity(edgeCount, ref track.ResizeOperations);
            
            var edgeSnapshot = data.EdgeScratch.AsSpan(0, edgeCount);
            for (var e = 0; e < edgeCount; e++)
            {
                edgeSnapshot[e] = edges[e];
            }

            // P.S. Two-Hop doesn't implement tracking. If re-enabled, implement it.
#if TwoHop
            // Expand the neighbors of the candidate:
            for (var i = 0; i < edgeSnapshot.Length; i++)
            {
                var neighbor = edgeSnapshot[i];
 
                if (data.IsVisited(neighbor))
                {
                    continue;
                }
 
                data.MarkVisited(neighbor);
 
                var neighborExcluded = predicate != null && !predicate(neighbor);
 
                if (neighborExcluded)
                {
                    // Traverse through excluded nodes but don't add them to results.
                    // Conditional two-hop: expand the excluded node's neighbors to maintain graph connectivity:
                    candidates.Enqueue(neighbor, VectorObjective.AdjustedCosineSimilarity(query, vectors[neighbor]!.VectorView));

                    // Skip two-hop expansion if the neighbor doesn't exist at this layer:
                    if (vectors[neighbor]!.TargetLayer < layer)
                    {
                        continue;
                    }

                    var twoHopEdges = vectors[neighbor]!.GetEdgesInLayer(layer);
                    var twoHopCount = twoHopEdges.Count;
                    data.EnsureScratchCapacity(twoHopCount);
                    var twoHopSnapshot = data.TwoHopScratch.AsSpan(0, twoHopCount);
                    for (var e = 0; e < twoHopCount; e++)
                    {
                        twoHopSnapshot[e] = twoHopEdges[e];
                    }

                    for (var j = 0; j < twoHopSnapshot.Length; j++)
                    {
                        var twoHopNeighbor = twoHopSnapshot[j];
 
                        if (data.IsVisited(twoHopNeighbor))
                        {
                            continue;
                        }
 
                        data.MarkVisited(twoHopNeighbor);
 
                        var twoHopScore = VectorObjective.AdjustedCosineSimilarity(query, vectors[twoHopNeighbor]!.VectorView);
                        var twoHopExcluded = !predicate!(twoHopNeighbor);
 
                        // Always add to candidates for traversal, even if excluded, to maintain connectivity:
                        candidates.Enqueue(twoHopNeighbor, twoHopScore);
 
                        if (!twoHopExcluded)
                        {
                            resultsQueue.TryPeek(out _, out var twoHopInverseWorstScore);
                            if (resultsQueue.Count < explorationFactor || twoHopScore < -twoHopInverseWorstScore)
                            {
                                resultsQueue.Enqueue(twoHopNeighbor, -twoHopScore);
 
                                // Discards the worst result:
                                if (resultsQueue.Count > explorationFactor)
                                {
                                    resultsQueue.Dequeue();
                                }
                            }
                        }
                    }
 
                    continue;
                }

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
#else
            // Expand the neighbors of the candidate:
            for (var i = 0; i < edgeSnapshot.Length; i++)
            {
                var neighbor = edgeSnapshot[i];
                
                if (data.IsVisited(neighbor, ref track.ResizeOperations))
                {
                    continue;
                }
                
                data.MarkVisited(neighbor, ref track.ResizeOperations);
                
                var neighborScore = VectorObjective.AdjustedCosineSimilarity(query, vectors[neighbor]!.VectorView);
                ++track.DistanceCalculations;
                
                resultsQueue.TryPeek(out _, out var currentInverseWorstScore);

                if (resultsQueue.Count < explorationFactor || neighborScore < -currentInverseWorstScore)
                {
                    candidates.Enqueue(neighbor, neighborScore);
                    ++track.CandidateEnqueueOperations;
                    
                    if (predicate == null || predicate(neighbor))
                    {
                        resultsQueue.Enqueue(neighbor, -neighborScore);
                        ++track.ResultEnqueueOperations;
                        
                        if (resultsQueue.Count > explorationFactor)
                        {
                            resultsQueue.Dequeue();
                            ++track.ResultDequeueOperations;
                        }
                    }
                }
            }
#endif
        }

        return track;
    }

    /// <summary>
    ///     Class for tracking the operations done by <see cref="Search"/>.
    /// </summary>
    public sealed class SearchInstrumentation
    {
        public int DescentResizeOperations;
        public SearchLayerTrackInfo SearchLayer;
    }

    /// <summary>
    ///     Searches for the approximate <see cref="k"/> vectrs most similar to <see cref="query"/>.
    /// </summary>
    /// <param name="query">A vector matching the <see cref="Dimension"/>.</param>
    /// <param name="k">The maximum number of vectors to explore.</param>
    /// <param name="efSearch">The exploration factor.</param>
    /// <param name="predicate">Filter.</param>
    /// <param name="instrumentation">Tracking for the operations done.</param>
    /// <returns>The found vectors.</returns>
    /// <exception cref="ArgumentException">Thrown if the <see cref="query"/>'s dimension does not match <see cref="Dimension"/>.</exception>
    public VectorSearchResult[] Search(ReadOnlySpan<float> query, int k, int efSearch = 200, Predicate<int>? predicate = null, SearchInstrumentation? instrumentation = null)
    {
        if (query.Length != Dimension)
        {
            throw new ArgumentException("Invalid query vector dimension");
        }

        if (k < 0)
        {
            throw new ArgumentException("K cannot be negative");
        }

        if (k == 0)
        {
            return [];
        }

        // Snapshot the entry point under _graphLock to ensure we see the highest-layer node and establish a happens-before edge with the first insertion's publication:
        StoredVectorImpl entryPoint;
        lock (_graphLock)
        {
            if (EntryPointVector == null)
            {
                return [];
            }

            entryPoint = EntryPointVector;
        }

        var searchData = _searchDataPool.Get();

        var descentResizeOperations = 0;
        
        try
        {
            // Ensure scratch buffers are sized for edge snapshots during greedy descent:
            searchData.EnsureScratchCapacity(Math.Max(MaxConnectionsDense, MaxConnectionsLane) + 1, ref descentResizeOperations);

            var currentNode = entryPoint;

            var currentScore = VectorObjective.AdjustedCosineSimilarity(query, currentNode.VectorView);
            for (var layerIndex = currentNode.TargetLayer; layerIndex > 0; layerIndex--)
            {
                while (true)
                {
                    var currentNodeEdges = currentNode.GetEdgesInLayer(layerIndex);
                    var edgeCount = currentNodeEdges.Count;
                    searchData.EnsureScratchCapacity(edgeCount, ref descentResizeOperations);
                    var edgeSnapshot = searchData.EdgeScratch.AsSpan(0, edgeCount);
                    for (var e = 0; e < edgeCount; e++)
                    {
                        edgeSnapshot[e] = currentNodeEdges[e];
                    }

                    var minimumChanged = false;

                    for (var i = 0; i < edgeSnapshot.Length; i++)
                    {
                        var neighborNode = VectorsInternal[edgeSnapshot[i]]!;
                        var neighborScore = VectorObjective.AdjustedCosineSimilarity(query, neighborNode.VectorView);

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
            }

            var searchLayerTrack = SearchLayer(searchData, query, currentNode, 0, Math.Max(k, efSearch), predicate);

            var queue = searchData.ResultsQueue;
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

            instrumentation?.DescentResizeOperations = descentResizeOperations;
            instrumentation?.SearchLayer = searchLayerTrack;
            
            return results;
        }
        finally
        {
            _searchDataPool.Return(searchData);
        }
    }
    
    private sealed class SearchData
    {
        public int[] Visited = [];
        public int VisitedGeneration = 1;
        public readonly PriorityQueue<int, float> CandidatesQueue = new();
        public readonly PriorityQueue<int, float> ResultsQueue = new();

        /// <summary>
        ///     Scratch buffer for edge snapshots in <see cref="SearchLayer"/>.
        ///     Owned by the pooled SearchData, so no per-call allocation.
        /// </summary>
        public int[] EdgeScratch = [];

#if TwoHop
        /// <summary>
        ///     Scratch buffer for two-hop edge snapshots in <see cref="SearchLayer"/>.
        ///     Separate from <see cref="EdgeScratch"/> because both can be live simultaneously.
        /// </summary>
        public int[] TwoHopScratch = [];
#endif

        public void Clear()
        {
            VisitedGeneration++;
            CandidatesQueue.Clear();
            ResultsQueue.Clear();
        }

        public bool EnsureCapacity(int capacity)
        {
            if (Visited.Length < capacity)
            {
                Visited = new int[Math.Max(capacity, Visited.Length * 2)];
                VisitedGeneration = 1;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Ensures the scratch buffers are at least <paramref name="capacity"/> in length.
        ///     Called once at the start of <see cref="SearchLayer"/>, outside the loop.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureScratchCapacity(int capacity, ref int resizeOperations)
        {
            if (EdgeScratch.Length < capacity)
            {
                EdgeScratch = new int[Math.Max(capacity, EdgeScratch.Length * 2)];
                ++resizeOperations;
            }
            
#if TwoHop
            if (TwoHopScratch.Length < capacity)
            {
                TwoHopScratch = new int[Math.Max(capacity, TwoHopScratch.Length * 2)];
                ++resizeOperations;
            }
#endif
        }
        
        /// <summary>
        ///     Ensures the scratch buffers are at least <paramref name="capacity"/> in length.
        ///     Called once at the start of <see cref="SearchLayer"/>, outside the loop.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureScratchCapacity(int capacity)
        {
            if (EdgeScratch.Length < capacity)
            {
                EdgeScratch = new int[Math.Max(capacity, EdgeScratch.Length * 2)];
            }
            
#if TwoHop
            if (TwoHopScratch.Length < capacity)
            {
                TwoHopScratch = new int[Math.Max(capacity, TwoHopScratch.Length * 2)];
            }
#endif
        }

        /// <summary>
        ///     Checks if the given index has been visited in the current generation.
        ///     If the index is beyond the current capacity (due to concurrent insertion), returns false
        ///     and grows the visited array to accommodate it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsVisited(int index, ref int resizeOperations)
        {
            if (index >= Visited.Length)
            {
                GrowVisited(index + 1);
                ++resizeOperations;
                return false;
            }

            return Visited[index] == VisitedGeneration;
        }

        /// <summary>
        ///     Marks the given index as visited in the current generation.
        ///     If the index is beyond the current capacity (due to concurrent insertion), grows the array first.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkVisited(int index, ref int resizeOperations)
        {
            if (index >= Visited.Length)
            {
                GrowVisited(index + 1);
                ++resizeOperations;
            }

            Visited[index] = VisitedGeneration;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowVisited(int requiredCapacity)
        {
            var newCapacity = Math.Max(requiredCapacity, Visited.Length * 2);
            var newVisited = new int[newCapacity];
            Array.Copy(Visited, newVisited, Visited.Length);
            Visited = newVisited;
        }
    }
}