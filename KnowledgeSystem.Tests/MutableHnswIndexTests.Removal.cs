using KnowledgeSystem.Vector;
using KnowledgeSystem.Vector.Hnsw;

// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Tests;

public partial class MutableHnswIndexTests
{
    [Fact]
    public void Remove_ValidVector_ReturnsTrue()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);
        var vector = new float[Dimension];
        vector[0] = 1f;
        var stored = index.Insert(vector);

        var result = index.Remove(stored);

        Assert.True(result);
    }

    [Fact]
    public void Remove_AlreadyRemoved_ReturnsFalse()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);
        var vector = new float[Dimension];
        vector[0] = 1f;
        var stored = index.Insert(vector);

        index.Remove(stored);
        var result = index.Remove(stored);

        Assert.False(result);
    }

    [Fact]
    public void Remove_LastVector_IndexIsEmpty()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);
        var vector = new float[Dimension];
        vector[0] = 1f;
        var stored = index.Insert(vector);

        index.Remove(stored);

        var query = new float[Dimension];
        query[0] = 1f;
        var results = index.Search(query, 10);
        Assert.Empty(results);
    }

    [Fact]
    public void Remove_EntryPoint_NewEntryPointSelected()
    {
        var (corpus, index) = BuildRandomCorpusWithIndex(100, seed: Seed);
        var entryPointIndex =
            index.VectorsInternal.First(v => v != null && v.TargetLayer == index.LayerCount - 1)!.Index;
        var entryPoint = index.Vectors[entryPointIndex]!;

        Assert.True(index.Remove(entryPoint));

        // Search should still work:
        var results = index.Search(corpus[0], 5);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void Remove_Repair_WhenCandidateEdgeListIsFull_DoesNotCorruptSavedIndex()
    {
        var index = new MutableHnswIndex(
            dimension: Dimension,
            maxConnectionsLane: 2,
            maxConnectionsDense: 2,
            efConstruction: 20,
            seed: Seed
        );

        var random = new Random(Seed);
        var vectors = new List<IStoredVector>();
        while (vectors.Count < 6)
        {
            var vector = index.Insert(GetTestVector(random, Dimension));

            if (index.VectorsInternal[vector.Index]!.TargetLayer == 0)
            {
                vectors.Add(vector);
            }
        }

        var removed = vectors[0];
        var neighbor = vectors[1];
        var intermediary = vectors[2];
        var candidate = vectors[3];
        var extra1 = vectors[4];
        var extra2 = vectors[5];

        foreach (var vector in index.VectorsInternal)
        {
            if (vector == null)
            {
                continue;
            }

            vector.GetEdgesInLayer(0).Clear();
        }

        var removedNode = index.VectorsInternal[removed.Index]!;
        var neighborNode = index.VectorsInternal[neighbor.Index]!;
        var intermediaryNode = index.VectorsInternal[intermediary.Index]!;
        var candidateNode = index.VectorsInternal[candidate.Index]!;
        var extraNode1 = index.VectorsInternal[extra1.Index]!;
        var extraNode2 = index.VectorsInternal[extra2.Index]!;

        removedNode.GetEdgesInLayer(0).Clear();
        removedNode.GetEdgesInLayer(0).Add(neighbor.Index);

        neighborNode.GetEdgesInLayer(0).Clear();
        neighborNode.GetEdgesInLayer(0).Add(removed.Index);
        neighborNode.GetEdgesInLayer(0).Add(intermediary.Index);

        intermediaryNode.GetEdgesInLayer(0).Clear();
        intermediaryNode.GetEdgesInLayer(0).Add(neighbor.Index);
        intermediaryNode.GetEdgesInLayer(0).Add(candidate.Index);

        candidateNode.GetEdgesInLayer(0).Clear();
        candidateNode.GetEdgesInLayer(0).Add(intermediary.Index);
        candidateNode.GetEdgesInLayer(0).Add(extra1.Index);
        candidateNode.GetEdgesInLayer(0).Add(extra2.Index);

        extraNode1.GetEdgesInLayer(0).Clear();
        extraNode1.GetEdgesInLayer(0).Add(candidate.Index);

        extraNode2.GetEdgesInLayer(0).Clear();
        extraNode2.GetEdgesInLayer(0).Add(candidate.Index);

        var removedCountBefore = index.Vectors.Count(v => v == null);
        Assert.True(index.Remove(removed));
        Assert.Equal(removedCountBefore + 1, index.Vectors.Count(v => v == null));

        using var stream = new MemoryStream();
        index.SaveToFile(stream);
        stream.Position = 0;

        var loaded = MutableHnswIndex.Load(stream);
        Assert.Null(loaded.Vectors[removed.Index]);

        var liveVector = loaded.VectorsInternal.First(v => v != null)!;
        var results = loaded.Search(liveVector.VectorView, 1);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void Remove_PreservesSymmetricEdges()
    {
        const int smallMaxConnections = 4;
        var random = new Random(Seed);
        var index = new MutableHnswIndex(
            dimension: Dimension,
            maxConnectionsLane: 4,
            maxConnectionsDense: smallMaxConnections,
            efConstruction: 20,
            seed: Seed
        );

        var vectors = new IStoredVector[100];
        for (var i = 0; i < 100; i++)
        {
            vectors[i] = index.Insert(GetTestVector(random, Dimension));
        }

        // Remove some vectors:
        for (var i = 0; i < 20; i++)
        {
            index.Remove(vectors[i * 3]);
        }

        // Verify all remaining edges are symmetric:
        var asymmetricPairs = new List<(int From, int To)>();

        for (var nodeIndex = 0; nodeIndex < index.VectorsInternal.Count; nodeIndex++)
        {
            var node = index.VectorsInternal[nodeIndex];

            if (node == null)
            {
                continue;
            }

            var edgeCount = node.GetEdgesInLayer(0).Count;
            for (var i = 0; i < edgeCount; i++)
            {
                var neighborIndex = node.GetEdgesInLayer(0)[i];
                var neighbor = index.VectorsInternal[neighborIndex];
                if (neighbor == null)
                {
                    asymmetricPairs.Add((From: nodeIndex, To: neighborIndex));
                    continue;
                }

                var neighborEdgeCount = neighbor.GetEdgesInLayer(0).Count;
                var found = false;
                for (var j = 0; j < neighborEdgeCount; j++)
                {
                    if (neighbor.GetEdgesInLayer(0)[j] == nodeIndex)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    asymmetricPairs.Add((From: nodeIndex, To: neighborIndex));
                }
            }
        }

        Assert.Empty(asymmetricPairs);
    }

    [Theory]
    [InlineData(500, 10, 50, 50)]
    [InlineData(500, 10, 50, 200)]
    public void Remove_RecallRemainsHigh(int vectorCount, int k, int queryCount, int removeCount)
    {
        var (corpus, index) = BuildRandomCorpusWithIndex(vectorCount, efConstruction: 200);
        var random = new Random(Seed);

        // Remove a portion of vectors:
        var removedSet = new HashSet<int>();
        for (var i = 0; i < removeCount; i++)
        {
            var stored = index.Vectors[i];
            if (stored != null)
            {
                index.Remove(stored);
                removedSet.Add(i);
            }
        }

        // Brute-force on the full corpus, then filter out removed indices:
        var totalRecall = 0.0;
        for (var q = 0; q < queryCount; q++)
        {
            var query = GetTestVector(random, Dimension);
            var hnswResults = index.Search(query, k, efSearch: 200);
            var bruteForceResults = BruteForceSearch(corpus, query, vectorCount)
                .Where(idx => !removedSet.Contains(idx))
                .Take(k)
                .ToHashSet();
            var hnswIndices = new HashSet<int>(hnswResults.Select(r => r.Index));
            var hits = hnswIndices.Count(bruteForceResults.Contains);

            totalRecall += (double)hits / k;
        }

        var averageRecall = totalRecall / queryCount;
        Assert.True(averageRecall >= 0.95, $"Bad average recall after removal: {averageRecall:P1}");
    }

    [Fact]
    public void Remove_SlotReuse_WorksCorrectly()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);
        var random = new Random(Seed);

        // ReSharper disable UnusedVariable
        var v1 = index.Insert(GetTestVector(random, Dimension));
        var v2 = index.Insert(GetTestVector(random, Dimension));
        var v3 = index.Insert(GetTestVector(random, Dimension));
        // ReSharper restore UnusedVariable

        var removedIndex = v2.Index;
        index.Remove(v2);

        var v4 = index.Insert(GetTestVector(random, Dimension));
        Assert.Equal(removedIndex, v4.Index);
        Assert.NotNull(index.VectorsInternal[v4.Index]);
    }

    [Fact]
    public void Remove_DeallocatesStorage()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);
        var random = new Random(Seed);

        var vectors = new IStoredVector[20];
        for (var i = 0; i < 20; i++)
        {
            vectors[i] = index.Insert(GetTestVector(random, Dimension));
        }

        for (var i = 0; i < 10; i++)
        {
            index.Remove(vectors[i]);
        }

        for (var i = 0; i < 10; i++)
        {
            var newVector = index.Insert(GetTestVector(random, Dimension));
            Assert.True(newVector.Index < 20, "New vector should reuse a dead slot");
        }
    }

    [Fact]
    public void Remove_MultipleRemoves_LayerCountAdjusts()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);
        var random = new Random(Seed);

        // Insert enough vectors to build multiple layers:
        var vectors = new List<IStoredVector>();
        for (var i = 0; i < 500; i++)
        {
            vectors.Add(index.Insert(GetTestVector(random, Dimension)));
        }

        var initialLayerCount = index.LayerCount;
        Assert.True(initialLayerCount > 1, "Expected multiple layers for 500 vectors");

        // Remove all vectors in the top layer:
        var topLayerVectors = vectors.Where(v => index.VectorsInternal[v.Index]?.TargetLayer == initialLayerCount - 1)
            .ToList();
        foreach (var v in topLayerVectors)
        {
            index.Remove(v);
        }

        Assert.True(index.LayerCount < initialLayerCount,
            "LayerCount should decrease after removing all top-layer vectors");
    }

    [Fact]
    public void Remove_AllVectors_IndexIsEmpty()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);
        var random = new Random(Seed);

        var vectors = new List<IStoredVector>();
        for (var i = 0; i < 20; i++)
        {
            vectors.Add(index.Insert(GetTestVector(random, Dimension)));
        }

        foreach (var v in vectors)
        {
            Assert.True(index.Remove(v));
        }

        // Search should return empty:
        var query = new float[Dimension];
        query[0] = 1f;
        Assert.Empty(index.Search(query, 5));

        // Can insert again after full removal:
        var newVector = index.Insert(GetTestVector(random, Dimension));
        var results = index.Search(newVector.VectorView.ToArray(), 1);
        Assert.Single(results);
        Assert.Equal(newVector.Index, results[0].Index);
    }

    [Fact]
    public void Remove_PreservesSymmetricEdges_AcrossAllLayers()
    {
        var random = new Random(Seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        var vectors = new List<IStoredVector>();
        for (var i = 0; i < 500; i++)
        {
            vectors.Add(index.Insert(GetTestVector(random, Dimension)));
        }

        // Remove some vectors:
        for (var i = 0; i < 50; i++)
        {
            index.Remove(vectors[i]);
        }

        // Check symmetry on every layer that exists:
        var asymmetricPairs = new List<(int From, int To, int Layer)>();

        for (var nodeIndex = 0; nodeIndex < index.VectorsInternal.Count; nodeIndex++)
        {
            var node = index.VectorsInternal[nodeIndex];
            if (node == null)
            {
                continue;
            }

            for (var layer = 0; layer <= node.TargetLayer; layer++)
            {
                var edges = node.GetEdgesInLayer(layer);
                for (var i = 0; i < edges.Count; i++)
                {
                    var neighborIndex = edges[i];
                    var neighbor = index.VectorsInternal[neighborIndex];
                    if (neighbor == null)
                    {
                        asymmetricPairs.Add((From: nodeIndex, To: neighborIndex, Layer: layer));
                        continue;
                    }

                    // Neighbor must exist in this layer:
                    if (neighbor.TargetLayer < layer)
                    {
                        asymmetricPairs.Add((From: nodeIndex, To: neighborIndex, Layer: layer));
                        continue;
                    }

                    var neighborEdges = neighbor.GetEdgesInLayer(layer);
                    var found = false;
                    for (var j = 0; j < neighborEdges.Count; j++)
                    {
                        if (neighborEdges[j] == nodeIndex)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        asymmetricPairs.Add((From: nodeIndex, To: neighborIndex, Layer: layer));
                    }
                }
            }
        }

        Assert.Empty(asymmetricPairs);
    }

    [Fact]
    public void Remove_SparseLayerVector_CleansUpSparseEdges()
    {
        var random = new Random(Seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        // Insert enough vectors to ensure some are on a sparse layer:
        var vectors = new List<IStoredVector>();
        for (var i = 0; i < 500; i++)
        {
            vectors.Add(index.Insert(GetTestVector(random, Dimension)));
        }

        var sparseVector = vectors.First(v => index.VectorsInternal[v.Index]!.TargetLayer > 0);
        var sparseNode = index.VectorsInternal[sparseVector.Index]!;
        var targetLayer = sparseNode.TargetLayer;

        // Collect its neighbors in the top sparse layer before removal:
        var sparseNeighborIndices = new List<int>();
        var sparseEdges = sparseNode.GetEdgesInLayer(targetLayer);
        for (var i = 0; i < sparseEdges.Count; i++)
        {
            sparseNeighborIndices.Add(sparseEdges[i]);
        }

        Assert.NotEmpty(sparseNeighborIndices);

        // Remove it:
        Assert.True(index.Remove(sparseVector));

        // Verify none of the former sparse neighbors still have an edge to the removed node:
        foreach (var edges in sparseNeighborIndices
                     .Select(neighborIdx => index.VectorsInternal[neighborIdx]!)
                     .Select(neighbor => neighbor.GetEdgesInLayer(targetLayer)))
        {
            for (var i = 0; i < edges.Count; i++)
            {
                Assert.NotEqual(sparseVector.Index, edges[i]);
            }
        }
    }

    [Fact]
    public void Remove_ThenInsertAtReusedSlot_IsSearchable()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);
        var random = new Random(Seed);

        var vectors = new IStoredVector[10];
        for (var i = 0; i < 10; i++)
        {
            vectors[i] = index.Insert(GetTestVector(random, Dimension));
        }

        var removedIndex = vectors[5].Index;
        index.Remove(vectors[5]);

        // Insert a new vector at the reused slot:
        var newData = new float[Dimension];
        newData[0] = 42f;
        var newVector = index.Insert(newData);

        Assert.Equal(removedIndex, newVector.Index);

        // Search for the new vector:
        var results = index.Search(newData, 1);
        Assert.NotEmpty(results);
        Assert.Equal(newVector.Index, results[0].Index);
    }

    [Fact]
    public void Remove_ThenInsert_MaintainsGraphIntegrity()
    {
        var random = new Random(Seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        var vectors = new List<IStoredVector>();
        for (var i = 0; i < 500; i++)
        {
            vectors.Add(index.Insert(GetTestVector(random, Dimension)));
        }

        AssertGraphIntegrity(index);

        // Remove a batch including sparse-layer nodes:
        for (var i = 0; i < 50; i++)
        {
            Assert.True(index.Remove(vectors[i]));
        }

        AssertGraphIntegrity(index);

        // Re-insert into freed slots:
        for (var i = 0; i < 50; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
        }

        AssertGraphIntegrity(index);
    }

    [Fact]
    public void Remove_SparseLayerNode_ThenInsert_MaintainsGraphIntegrity()
    {
        var random = new Random(Seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        var vectors = new List<IStoredVector>();
        for (var i = 0; i < 500; i++)
        {
            vectors.Add(index.Insert(GetTestVector(random, Dimension)));
        }

        // Remove only nodes that have sparse layers:
        var sparseVectors = vectors
            .Where(v => index.VectorsInternal[v.Index]!.TargetLayer > 0)
            .Take(20)
            .ToList();

        Assert.NotEmpty(sparseVectors);

        foreach (var v in sparseVectors)
        {
            Assert.True(index.Remove(v));
        }

        AssertGraphIntegrity(index);

        // Insert new vectors, some will reuse freed slots:
        for (var i = 0; i < 20; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
        }

        AssertGraphIntegrity(index);
    }

    [Fact]
    public void SaveLoad_RemoveThenInsert_MaintainsGraphIntegrity()
    {
        var random = new Random(Seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        var vectors = new List<IStoredVector>();
        for (var i = 0; i < 500; i++)
        {
            vectors.Add(index.Insert(GetTestVector(random, Dimension)));
        }

        // Remove some vectors:
        for (var i = 0; i < 30; i++)
        {
            index.Remove(vectors[i]);
        }

        // Save and reload:
        using var stream = new MemoryStream();
        index.SaveToFile(stream);
        stream.Position = 0;
        var loaded = MutableHnswIndex.Load(stream);

        AssertGraphIntegrity(loaded);

        // Now insert into the loaded index:
        for (var i = 0; i < 30; i++)
        {
            loaded.Insert(GetTestVector(random, Dimension));
        }

        AssertGraphIntegrity(loaded);
    }

    [Theory]
    [InlineData(48, 500)]
    [InlineData(16, 500)]
    public void Remove_EachStep_MaintainsGraphIntegrity(int seed, int count)
    {
        var random = new Random(seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: seed);

        var vectors = new List<IStoredVector>();
        for (var i = 0; i < count; i++)
        {
            vectors.Add(index.Insert(GetTestVector(random, Dimension)));
        }

        AssertGraphIntegrity(index);

        var removeCount = count / 10;
        for (var i = 0; i < removeCount; i++)
        {
            Assert.True(index.Remove(vectors[i]), $"Remove failed at index {i}");
            AssertGraphIntegrity(index);
        }
    }

    [Theory]
    [InlineData(48, 500)]
    [InlineData(16, 500)]
    public void Remove_ThenInsert_DoesNotAccessInvalidLayer(int seed, int count)
    {
        var random = new Random(seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: seed);

        var vectors = new List<IStoredVector>();
        for (var i = 0; i < count; i++)
        {
            vectors.Add(index.Insert(GetTestVector(random, Dimension)));
        }

        var removeCount = count / 10;
        for (var i = 0; i < removeCount; i++)
        {
            index.Remove(vectors[i]);
        }

        output.WriteLine($"Seed: {seed}: LayerCount: {index.LayerCount}, EntryPoint.TargetLayer: {index.EntryPointVector?.TargetLayer}");
        var maxLayer = 0;
        for (var i = 0; i < index.VectorsInternal.Count; i++)
        {
            var node = index.VectorsInternal[i];
            if (node != null && node.TargetLayer > maxLayer)
            {
                maxLayer = node.TargetLayer;
            }
        }

        output.WriteLine($"Actual max TargetLayer: {maxLayer}");

        // This should not throw:
        for (var i = 0; i < removeCount; i++)
        {
            try
            {
                index.Insert(GetTestVector(random, Dimension));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                output.WriteLine($"Crashed on insert #{i}: {ex.Message}");
                throw;
            }
        }
    }

    [Theory]
    [InlineData(48, 500)]
    [InlineData(16, 500)]
    public void Remove_ThenInsert_EachStep_MaintainsGraphIntegrity(int seed, int count)
    {
        var random = new Random(seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: seed);

        var vectors = new List<IStoredVector>();
        for (var i = 0; i < count; i++)
        {
            vectors.Add(index.Insert(GetTestVector(random, Dimension)));
        }

        var removeCount = count / 10;
        for (var i = 0; i < removeCount; i++)
        {
            index.Remove(vectors[i]);
        }

        for (var i = 0; i < removeCount; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
            AssertGraphIntegrity(index);
        }
    }

    [Theory]
    [InlineData(1, 500)]
    [InlineData(2, 500)]
    [InlineData(3, 500)]
    [InlineData(4, 500)]
    [InlineData(5, 500)]
    [InlineData(6, 500)]
    [InlineData(7, 500)]
    [InlineData(8, 500)]
    [InlineData(9, 500)]
    [InlineData(10, 500)]
    [InlineData(11, 500)]
    [InlineData(12, 500)]
    [InlineData(13, 500)]
    [InlineData(14, 500)]
    [InlineData(15, 500)]
    [InlineData(16, 500)]
    [InlineData(17, 500)]
    [InlineData(18, 500)]
    [InlineData(19, 500)]
    [InlineData(20, 500)]
    [InlineData(21, 500)]
    [InlineData(22, 500)]
    [InlineData(23, 500)]
    [InlineData(24, 500)]
    [InlineData(25, 500)]
    [InlineData(26, 500)]
    [InlineData(27, 500)]
    [InlineData(28, 500)]
    [InlineData(29, 500)]
    [InlineData(30, 500)]
    [InlineData(31, 500)]
    [InlineData(32, 500)]
    [InlineData(33, 500)]
    [InlineData(34, 500)]
    [InlineData(35, 500)]
    [InlineData(36, 500)]
    [InlineData(37, 500)]
    [InlineData(38, 500)]
    [InlineData(39, 500)]
    [InlineData(40, 500)]
    [InlineData(41, 500)]
    [InlineData(42, 500)]
    [InlineData(43, 500)]
    [InlineData(44, 500)]
    [InlineData(45, 500)]
    [InlineData(46, 500)]
    [InlineData(47, 500)]
    [InlineData(48, 500)]
    [InlineData(49, 500)]
    [InlineData(50, 500)]
    public void SaveLoad_RemoveThenInsert_ManySeeds_MaintainsGraphIntegrity(int seed, int count)
    {
        var random = new Random(seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: seed);

        var vectors = new List<IStoredVector>();
        for (var i = 0; i < count; i++)
        {
            vectors.Add(index.Insert(GetTestVector(random, Dimension)));
        }

        var removeCount = count / 10;
        for (var i = 0; i < removeCount; i++)
        {
            index.Remove(vectors[i]);
        }

        using var stream = new MemoryStream();
        index.SaveToFile(stream);
        stream.Position = 0;
        var loaded = MutableHnswIndex.Load(stream);

        AssertGraphIntegrity(loaded);

        for (var i = 0; i < removeCount; i++)
        {
            loaded.Insert(GetTestVector(random, Dimension));
        }

        AssertGraphIntegrity(loaded);
    }
}