using KnowledgeSystem.Vector;
using KnowledgeSystem.Vector.Hnsw;

// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable LoopCanBeConvertedToQuery

namespace KnowledgeSystem.Tests;

public partial class MutableHnswIndexTests
{
    [Fact]
    public void SaveLoad_EmptyIndex_RoundTrips()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);

        using var stream = new MemoryStream();
        index.SaveToFile(stream);
        stream.Position = 0;

        var loaded = MutableHnswIndex.Load(stream);

        Assert.Equal(index.Dimension, loaded.Dimension);
        Assert.Equal(index.MaxConnectionsLane, loaded.MaxConnectionsLane);
        Assert.Equal(index.MaxConnectionsDense, loaded.MaxConnectionsDense);
        Assert.Equal(index.ExplorationFactorConstruction, loaded.ExplorationFactorConstruction);
        Assert.Equal(index.LayerCount, loaded.LayerCount);
        Assert.Empty(loaded.Vectors);
    }

    [Fact]
    public void SaveLoad_SingleVector_RoundTrips()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);
        var vector = new float[Dimension];
        vector[0] = 1f;
        index.Insert(vector);

        using var stream = new MemoryStream();
        index.SaveToFile(stream);
        stream.Position = 0;

        var loaded = MutableHnswIndex.Load(stream);

        Assert.Single(loaded.Vectors);
        Assert.Equal(1, loaded.LayerCount);

        var results = loaded.Search(vector, 1);
        Assert.Single(results);
        Assert.Equal(0, results[0].Index);
    }

    [Theory]
    [InlineData(100, 10)]
    [InlineData(500, 20)]
    public void SaveLoad_PreservedIndex_MaintainsRecall(int vectorCount, int k)
    {
        var (corpus, index) = BuildRandomCorpusWithIndex(vectorCount, efConstruction: 200);

        using var stream = new MemoryStream();
        index.SaveToFile(stream);
        stream.Position = 0;

        var loaded = MutableHnswIndex.Load(stream);

        var random = new Random(Seed + 1);
        var totalRecallOriginal = 0.0;
        var totalRecallLoaded = 0.0;
        const int queryCount = 20;

        for (var q = 0; q < queryCount; q++)
        {
            var query = GetTestVector(random, Dimension);

            var originalResults = index.Search(query, k, efSearch: 20);
            var loadedResults = loaded.Search(query, k, efSearch: 20);
            var bruteForceResults = BruteForceSearch(corpus, query, k);

            var originalIndices = new HashSet<int>(originalResults.Select(r => r.Index));
            var loadedIndices = new HashSet<int>(loadedResults.Select(r => r.Index));

            totalRecallOriginal += (double)bruteForceResults.Count(originalIndices.Contains) / k;
            totalRecallLoaded += (double)bruteForceResults.Count(loadedIndices.Contains) / k;
        }

        var avgRecallOriginal = totalRecallOriginal / queryCount;
        var avgRecallLoaded = totalRecallLoaded / queryCount;

        Assert.True(Math.Abs(avgRecallOriginal - avgRecallLoaded) < 1e-5,
            $"Recall mismatch: original {avgRecallOriginal:P1}, loaded {avgRecallLoaded:P1}");
    }

    [Fact]
    public void SaveLoad_WithRemovals_PreservesFreeSlots()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 100, seed: Seed);
        var random = new Random(Seed);

        var vectors = new IStoredVector[20];
        for (var i = 0; i < 20; i++)
        {
            vectors[i] = index.Insert(GetTestVector(random, Dimension));
        }

        index.Remove(vectors[5]);
        index.Remove(vectors[10]);
        index.Remove(vectors[15]);

        using var stream = new MemoryStream();
        index.SaveToFile(stream);
        stream.Position = 0;

        var loaded = MutableHnswIndex.Load(stream);

        Assert.Null(loaded.Vectors[5]);
        Assert.Null(loaded.Vectors[10]);
        Assert.Null(loaded.Vectors[15]);

        var newVector = loaded.Insert(GetTestVector(random, Dimension));
        Assert.True(newVector.Index is 5 or 10 or 15, $"Expected reused slot, got index {newVector.Index}");

        var query = GetTestVector(random, Dimension);
        var results = loaded.Search(query, 5, efSearch: 100);
        Assert.True(results.Length > 0);
    }

    [Fact]
    public void SaveLoad_InvalidMagic_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream();
        stream.WriteByte((byte)'X');
        stream.WriteByte((byte)'Y');
        stream.WriteByte((byte)'Z');
        stream.WriteByte((byte)'W');
        stream.Position = 0;

        Assert.Throws<InvalidDataException>(() => MutableHnswIndex.Load(stream));
    }

    [Fact]
    public void SaveLoad_FilePath_RoundTrips()
    {
        var (_, index) = BuildRandomCorpusWithIndex(50, efConstruction: 100);
        var path = Path.GetTempFileName();

        try
        {
            index.Save(path);

            var loaded = MutableHnswIndex.LoadFromFile(path);

            Assert.Equal(index.Dimension, loaded.Dimension);
            Assert.Equal(index.MaxConnectionsLane, loaded.MaxConnectionsLane);
            Assert.Equal(index.MaxConnectionsDense, loaded.MaxConnectionsDense);
            Assert.Equal(index.Vectors.Count, loaded.Vectors.Count);

            var random = new Random(Seed + 1);
            var query = GetTestVector(random, Dimension);
            var originalResults = index.Search(query, 5, efSearch: 100);
            var loadedResults = loaded.Search(query, 5, efSearch: 100);

            Assert.Equal(originalResults.Length, loadedResults.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IncrementalSave_InsertAfterSave_RoundTrips()
    {
        var random = new Random(Seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        for (var i = 0; i < 200; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
        }

        using var stream = new MemoryStream();
        index.SaveToFile(stream);
        var fullSaveLength = stream.Length;

        for (var i = 0; i < 50; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
        }

        index.SaveToFile(stream);
        var incrementalSaveLength = stream.Length;

        Assert.True(incrementalSaveLength > fullSaveLength, "File should grow after inserting new vectors");

        stream.Position = 0;
        var loaded = MutableHnswIndex.Load(stream);
        AssertGraphIntegrity(loaded);

        var query = GetTestVector(random, Dimension);
        var originalResults = index.Search(query, 10, efSearch: 200);
        var loadedResults = loaded.Search(query, 10, efSearch: 200);
        Assert.Equal(originalResults.Length, loadedResults.Length);
    }

    [Fact]
    public void IncrementalSave_RemoveAfterSave_RoundTrips()
    {
        var random = new Random(Seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        var vectors = new List<IStoredVector>();
        for (var i = 0; i < 200; i++)
        {
            vectors.Add(index.Insert(GetTestVector(random, Dimension)));
        }

        using var stream = new MemoryStream();
        index.SaveToFile(stream);

        for (var i = 0; i < 20; i++)
        {
            index.Remove(vectors[i]);
        }

        index.SaveToFile(stream);

        stream.Position = 0;
        var loaded = MutableHnswIndex.Load(stream);
        AssertGraphIntegrity(loaded);

        for (var i = 0; i < 20; i++)
        {
            Assert.Null(loaded.Vectors[vectors[i].Index]);
        }
    }

    [Fact]
    public void IncrementalSave_MultipleSaves_AccumulateCorrectly()
    {
        var random = new Random(Seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        for (var i = 0; i < 100; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
        }

        using var stream = new MemoryStream();
        index.SaveToFile(stream);

        for (var i = 0; i < 20; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
        }

        index.SaveToFile(stream);

        for (var i = 0; i < 20; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
        }

        index.SaveToFile(stream);

        stream.Position = 0;
        var loaded = MutableHnswIndex.Load(stream);
        AssertGraphIntegrity(loaded);

        var liveCount = 0;
        for (var i = 0; i < loaded.VectorsInternal.Count; i++)
        {
            if (loaded.VectorsInternal[i] != null)
            {
                liveCount++;
            }
        }

        Assert.Equal(140, liveCount);
    }

    [Fact]
    public void IncrementalSave_NoChanges_IsNoOp()
    {
        var random = new Random(Seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        for (var i = 0; i < 100; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
        }

        using var stream = new MemoryStream();
        index.SaveToFile(stream);
        var lengthAfterFirstSave = stream.Length;

        index.SaveToFile(stream);
        var lengthAfterSecondSave = stream.Length;

        Assert.Equal(lengthAfterFirstSave, lengthAfterSecondSave);
    }

    [Fact]
    public void IncrementalSave_FullRewriteOnLayerOverflow()
    {
        var random = new Random(Seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        for (var i = 0; i < 100; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
        }

        using var stream = new MemoryStream();
        index.SaveToFile(stream);

        var initialMaxLayersAllocated = index.MaxLayersAllocated;

        for (var i = 0; i < 2000; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
        }

        index.SaveToFile(stream);

        if (index.LayerCount > initialMaxLayersAllocated)
        {
            Assert.True(index.MaxLayersAllocated >= index.LayerCount,
                $"MaxLayersAllocated ({index.MaxLayersAllocated}) should be >= LayerCount ({index.LayerCount}) after rewrite");
        }

        stream.Position = 0;
        var loaded = MutableHnswIndex.Load(stream);
        AssertGraphIntegrity(loaded);
    }

    [Fact]
    public void IncrementalSave_FileOffsetPopulatedOnLoad()
    {
        var random = new Random(Seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        for (var i = 0; i < 50; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
        }

        using var stream = new MemoryStream();
        index.SaveToFile(stream);
        stream.Position = 0;
        var loaded = MutableHnswIndex.Load(stream);

        for (var i = 0; i < loaded.VectorsInternal.Count; i++)
        {
            var vector = loaded.VectorsInternal[i];

            if (vector != null)
            {
                Assert.True(vector.FileOffset > 0, $"Vector {i} should have a non-zero FileOffset");
            }
        }
    }

    [Fact]
    public void IncrementalSave_InsertRemoveInsert_RoundTrips()
    {
        var random = new Random(Seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        var vectors = new List<IStoredVector>();
        for (var i = 0; i < 200; i++)
        {
            vectors.Add(index.Insert(GetTestVector(random, Dimension)));
        }

        using var stream = new MemoryStream();
        index.SaveToFile(stream);

        for (var i = 0; i < 20; i++)
        {
            index.Remove(vectors[i]);
        }

        index.SaveToFile(stream);

        for (var i = 0; i < 20; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
        }

        index.SaveToFile(stream);

        stream.Position = 0;
        var loaded = MutableHnswIndex.Load(stream);
        AssertGraphIntegrity(loaded);

        var query = GetTestVector(random, Dimension);
        var originalResults = index.Search(query, 10, efSearch: 200);
        var loadedResults = loaded.Search(query, 10, efSearch: 200);
        Assert.Equal(originalResults.Length, loadedResults.Length);
    }

    [Fact]
    public void IncrementalSave_RecallPreserved()
    {
        var (corpus, index) = BuildRandomCorpusWithIndex(500, efConstruction: 200);

        using var stream = new MemoryStream();
        index.SaveToFile(stream);

        var random = new Random(Seed + 99);
        var additionalCorpus = new List<float[]>();
        for (var i = 0; i < 100; i++)
        {
            var vector = GetTestVector(random, Dimension);
            additionalCorpus.Add(vector);
            index.Insert(vector);
        }

        index.SaveToFile(stream);

        stream.Position = 0;
        var loaded = MutableHnswIndex.Load(stream);

        var fullCorpus = corpus.Concat(additionalCorpus).ToArray();

        var queryRandom = new Random(Seed + 1);
        var totalRecall = 0.0;
        const int queryCount = 20;
        const int k = 10;

        for (var q = 0; q < queryCount; q++)
        {
            var query = GetTestVector(queryRandom, Dimension);
            var loadedResults = loaded.Search(query, k, efSearch: 200);
            var bruteForceResults = BruteForceSearch(fullCorpus, query, k);
            var loadedIndices = new HashSet<int>(loadedResults.Select(r => r.Index));
            totalRecall += (double)bruteForceResults.Count(loadedIndices.Contains) / k;
        }

        var averageRecall = totalRecall / queryCount;
        Assert.True(averageRecall >= 0.90, $"Bad recall after incremental save: {averageRecall:P1}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(48)]
    public void IncrementalSave_Stress_InsertRemoveSaveLoad_CyclesRoundTrip(int seed)
    {
        var random = new Random(seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: seed);

        var liveIndices = new List<int>();
        for (var i = 0; i < 200; i++)
        {
            liveIndices.Add(index.Insert(GetTestVector(random, Dimension)).Index);
        }

        using var stream = new MemoryStream();
        index.SaveToFile(stream);

        for (var cycle = 0; cycle < 6; cycle++)
        {
            const int batchSize = 10;

            for (var i = 0; i < batchSize; i++)
            {
                var liveIndex = liveIndices[random.Next(liveIndices.Count)];
                var vector = index.VectorsInternal[liveIndex];

                if (vector == null)
                {
                    liveIndices.Remove(liveIndex);
                    i--;
                    continue;
                }

                Assert.True(index.Remove(vector));
                liveIndices.Remove(liveIndex);
            }

            for (var i = 0; i < batchSize; i++)
            {
                liveIndices.Add(index.Insert(GetTestVector(random, Dimension)).Index);
            }

            AssertGraphIntegrity(index);

            index.SaveToFile(stream);
            stream.Position = 0;
            index = MutableHnswIndex.Load(stream);

            AssertGraphIntegrity(index);

            liveIndices = index.VectorsInternal
                .Select((vector, vectorIndex) => vector == null ? -1 : vectorIndex)
                .Where(vectorIndex => vectorIndex >= 0)
                .ToList();
        }

        var query = GetTestVector(random, Dimension);
        var results = index.Search(query, 10, efSearch: 200);
        Assert.NotEmpty(results);
    }
}