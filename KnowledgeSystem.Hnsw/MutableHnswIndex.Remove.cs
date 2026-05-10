// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable LoopCanBeConvertedToQuery

namespace KnowledgeSystem.Hnsw;

public sealed partial class MutableHnswIndex
{
    /// <summary>
    ///     Removes a vector from the index, deallocates its storage, and repairs the graph by re-searching from affected neighbors.
    /// </summary>
    /// <param name="vector">The vector to remove. Must be a vector previously returned by <see cref="Insert"/>.</param>
    /// <returns>True if the vector was found and removed. False if it was already removed or not part of this index.</returns>
    public bool Remove(IStoredVector vector)
    {
        if (vector.Index < 0 || vector.Index >= VectorsInternal.Count)
        {
            return false;
        }

        var node = VectorsInternal[vector.Index];

        if (node == null || !ReferenceEquals(node, vector))
        {
            return false;
        }

        // ReSharper disable InlineTemporaryVariable
        var vectors = VectorsInternal;
        var neighborSnapshotBuffer = _neighborSnapshotBuffer;
        var resultsBuffer = _resultsBuffer;
        // ReSharper restore InlineTemporaryVariable
        
        // For each layer the node participates in, remove reverse edges and search-repair neighbors:
        for (var layer = 0; layer <= node.TargetLayer; layer++)
        {
            var nodeEdges = node.GetEdgesInLayer(layer);
            var maxConnections = layer == 0 ? MaxConnectionsDense : MaxConnectionsLane;

            // Copy the neighbor indices (edges will be modified during repair):
            var neighborCount = nodeEdges.Count;
            neighborSnapshotBuffer.Clear();
            for (var neighborIndex = 0; neighborIndex < neighborCount; neighborIndex++)
            {
                neighborSnapshotBuffer.Add(nodeEdges[neighborIndex]);
            }

            // Remove reverse edges from all neighbors:
            for (var i = 0; i < neighborCount; i++)
            {
                vectors[neighborSnapshotBuffer[i]]!.GetEdgesInLayer(layer).Remove(node.Index);
            }

            // Repair using search.
            // For each neighbor that lost the edge, re-search from it to find the optimal new connection.
            // This is similar to insertion and should preserve graph quality much better than e.g. just connecting the neighbors to each other.
            // The cost high, though.
            for (var neighborIndex = 0; neighborIndex < neighborCount; neighborIndex++)
            {
                var neighbor = vectors[neighborSnapshotBuffer[neighborIndex]]!;
                var neighborEdges = neighbor.GetEdgesInLayer(layer);

                if (neighborEdges.Count >= maxConnections)
                {
                    continue;
                }

                // Search from the neighbor's own position to find its best candidates:
                SearchLayer(_searchData, neighbor.VectorView, neighbor, layer, ExplorationFactorConstruction);

                var resultsQueue = _searchData.ResultsQueue;
                resultsBuffer.Clear();
                
                while (resultsQueue.TryDequeue(out var element, out var inverseScore))
                {
                    resultsBuffer.Add(new ScoredResult(element, -inverseScore));
                }

                // Add new edges from search results, skipping junk:
                for (var j = resultsBuffer.Count - 1; j >= 0; j--)
                {
                    var candidateIndex = resultsBuffer[j].Index;
                    if (candidateIndex == neighbor.Index || candidateIndex == node.Index)
                    {
                        continue;
                    }

                    // Skip if already connected:
                    var alreadyConnected = false;
                    for (var k = 0; k < neighborEdges.Count; k++)
                    {
                        if (neighborEdges[k] == candidateIndex)
                        {
                            alreadyConnected = true;
                            break;
                        }
                    }

                    if (alreadyConnected)
                    {
                        continue;
                    }

                    neighborEdges.Add(candidateIndex);
                    var candidateNode = vectors[candidateIndex]!;
                    var candidateEdges = candidateNode.GetEdgesInLayer(layer);
                    candidateEdges.Add(neighbor.Index);

                    if (candidateEdges.Count > maxConnections)
                    {
                        TrimEdges(_trimEdgesData, candidateNode, layer, maxConnections);
                    }

                    if (neighborEdges.Count >= maxConnections)
                    {
                        break;
                    }
                }

                if (neighborEdges.Count > maxConnections)
                {
                    TrimEdges(_trimEdgesData, neighbor, layer, maxConnections);
                }
            }
        }

        // If the vector was the entry point, we will replace it:
        if (EntryPointVector == node)
        {
            SelectNewEntryPoint(node);
        }

        // Deallocate:
        
        if (node.SparseGraphs.HasValue)
        {
            var sparseSpan = node.SparseGraphs.Value.Block.Span;
            for (var i = 0; i < sparseSpan.Length; i++)
            {
                sparseSpan[i].Storage.Deallocate();
            }

            node.SparseGraphs.Value.Deallocate();
        }

        node.DenseGraph.Storage.Deallocate();
        node.VectorStorage.Deallocate();
        
        VectorsInternal[node.Index] = null;
        _freeSlots.Push(node.Index);

        return true;
    }

    /// <summary>
    ///     Selects a new entry point after the current one was removed.
    ///     Starts from the removed node's highest layer and looks for neighbors (which are also in that layer).
    /// </summary>
    private void SelectNewEntryPoint(StoredVectorImpl removedNode)
    {
        for (var layer = removedNode.TargetLayer; layer >= 0; layer--)
        {
            var edges = removedNode.GetEdgesInLayer(layer);

            if (edges.Count > 0)
            {
                var candidate = VectorsInternal[edges[0]]!;
                EntryPointVector = candidate;
                LayerCount = candidate.TargetLayer + 1;
                return;
            }

            // No neighbors in this layer. This must mean the node was alone on the layer:
            if (layer > 0)
            {
                LayerCount = layer;
            }
        }

        // No neighbors in any layer, the index is empty:
        EntryPointVector = null;
        LayerCount = 1;
    }
}
