using System.Diagnostics;
using System.Numerics.Tensors;
using KnowledgeSystem.VectorDatabase;
using Xunit.Abstractions;
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Tests;

public class MutableHnswIndexTests(ITestOutputHelper output)
{
    private const int Seed = 3141;
    private const int Dimension = 512;

    #region Random Generator

    /// <summary>
    ///     Creates a random vector for testing. Each component is in the range <c>[-1, 1]</c>.
    /// </summary>
    /// <returns></returns>
    private static float[] GetTestVector(Random random, int dimension, bool normalized = true)
    {
        var vector = new float[dimension];
        var normSqr = 0.0f;
        for (var i = 0; i < dimension; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2 - 1);
            normSqr += vector[i] * vector[i];
        }

        if (normSqr < 1e-8)
        {
            Assert.Fail("Encountered zero norm randomly generated vector");
        }
        
        if (normalized)
        {
            var k = 1.0f / MathF.Sqrt(normSqr);
            for (var i = 0; i < dimension; i++)
            {
                vector[i] *= k;
            }
        }
        
        return vector;
    }

    #endregion
    
    #region Helper

    /// <summary>
    ///     Builds a random corpus array of test vectors.
    /// </summary>
    private static float[][] BuildRandomCorpusArray(int count, int seed = Seed)
    {
        var random = new Random(seed);
        var corpus = new float[count][];
        
        for (var i = 0; i < count; i++)
        {
            corpus[i] = GetTestVector(random, Dimension);
        }
        
        return corpus;
    }

    /// <summary>
    ///     Builds a random corpus and inserts all vectors into the index.
    /// </summary>
    private static (float[][] corpus, MutableHnswIndex index) BuildRandomCorpusWithIndex(int count, int efConstruction = 200, int seed = Seed)
    {
        var corpus = BuildRandomCorpusArray(count, seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction, seed: seed);
        
        for (var i = 0; i < count; i++)
        {
            index.Insert(corpus[i]);
        }
        
        return (corpus, index);
    }

    /// <summary>
    ///     Generates multiple random query vectors.
    /// </summary>
    private static float[][] GenerateRandomQueries(int count, int seed = Seed)
    {
        var random = new Random(seed);
        var queries = new float[count][];
        for (var q = 0; q < count; q++)
        {
            queries[q] = GetTestVector(random, Dimension);
        }
        return queries;
    }

    /// <summary>
    ///     Brute-force search for the exact K best vectors.
    /// </summary>
    private static int[] BruteForceSearch(float[][] corpus, float[] query, int k) => corpus
        .Select((v, i) => (Index: i, Score: 1.0f - TensorPrimitives.CosineSimilarity(v, query)))
        .OrderBy(x => x.Score)
        .Take(k)
        .Select(x => x.Index)
        .ToArray();

    #endregion
    
    [Fact]
    public void Constructor_MaxConnectionsLaneOne_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MutableHnswIndex(Dimension, maxConnectionsLane: 1, maxConnectionsDense: 32, seed: Seed)
        );
    }
    
    [Fact]
    public void Constructor_MaxConnectionsLaneZero_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MutableHnswIndex(Dimension, maxConnectionsLane: 0, maxConnectionsDense: 32, seed: Seed)
        );
    }
    
    #region Search
    
    /// <summary>
    ///     Builds an index with random vectors, then queries it and compares results against a brute-force search.
    ///     Asserts that recall is at least 95%.
    /// </summary>
    [Theory]
    [InlineData(500, 10, 50)]
    [InlineData(1000, 5, 100)]
    [InlineData(200, 20, 20)]
    public void Search_MatchesBruteForce_WithHighRecall(int vectorCount, int k, int queryCount)
    {
        var (corpus, index) = BuildRandomCorpusWithIndex(vectorCount, efConstruction: 200);
        var random = new Random(Seed);

        // Generate queries and measure recall:
        var totalRecall = 0.0;
        for (var q = 0; q < queryCount; q++)
        {
            var query = GetTestVector(random, Dimension);
            var hnswResults = index.Search(query, k, efSearch: 200);
            var bruteForceResults = BruteForceSearch(corpus, query, k);
            var hnswIndices = new HashSet<int>(hnswResults.Select(r => r.Index));
            var hits = bruteForceResults.Count(hnswIndices.Contains);

            totalRecall += (double)hits / k;
        }

        var averageRecall = totalRecall / queryCount;
        Assert.True(averageRecall >= 0.95, $"Bad average recall {averageRecall:P1}");
    }

    [Fact]
    public void Search_EmptyIndex_ReturnsEmptyArray()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);
        var query = new float[Dimension];
        query[0] = 1f;

        var results = index.Search(query, 10);

        Assert.Empty(results);
    }

    [Fact]
    public void Search_SingleVector_ReturnsThatVector()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);
        var vector = new float[Dimension];
        vector[0] = 1f;
        index.Insert(vector);

        var results = index.Search(vector, 5);

        Assert.Single(results);
        Assert.Equal(0, results[0].Index);
    }

    [Fact]
    public void Search_KLargerThanCorpus_ReturnsAllVectors()
    {
        const int corpusSize = 10;
        var (_, index) = BuildRandomCorpusWithIndex(corpusSize);
        var random = new Random(Seed);

        var query = GetTestVector(random, Dimension);
        var results = index.Search(query, 100);

        Assert.Equal(corpusSize, results.Length);
        Assert.Equal(corpusSize, results.Select(r => r.Index).Distinct().Count());
    }

    [Fact]
    public void Search_ExactMatch_IsTopResult()
    {
        var (corpus, index) = BuildRandomCorpusWithIndex(100, efConstruction: 200);

        const int targetIndex = 50;
        var results = index.Search(corpus[targetIndex], 5);

        Assert.Equal(targetIndex, results[0].Index);
        Assert.True(results[0].Score < 1e-5f, $"Error in the self score, got {results[0].Score}");
    }

    [Fact]
    public void Search_ResultsAreSortedByScoreAscending()
    {
        var (_, index) = BuildRandomCorpusWithIndex(200, efConstruction: 200);
        var random = new Random(Seed);

        var query = GetTestVector(random, Dimension);
        var results = index.Search(query, 20);

        for (var i = 1; i < results.Length; i++)
        {
            Assert.True(
                results[i].Score >= results[i - 1].Score,
                $"Results not sorted: index {i - 1} score {results[i - 1].Score} > index {i} score {results[i].Score}"
            );
        }
    }
    
    [Fact]
    public void Search_WrongDimension_Throws()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);
        index.Insert(new float[Dimension]);

        Assert.Throws<ArgumentException>(() => index.Search(new float[Dimension + 1], 5));
    }

    [Fact]
    public void Search_NegativeK_Throws()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);
        index.Insert(new float[Dimension]);

        Assert.Throws<ArgumentException>(() => index.Search(new float[Dimension], -1));
    }

    [Fact]
    public void Search_ZeroK_ReturnsEmpty()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);
        index.Insert(new float[Dimension]);

        var results = index.Search(new float[Dimension], 0);

        Assert.Empty(results);
    }

    [Fact]
    public void Search_SeededIndex_IsReproducible()
    {
        var query = GetTestVector(new Random(99), Dimension);

        var run1 = BuildAndSearch(123);
        var run2 = BuildAndSearch(123);

        Assert.Equal(run1.Length, run2.Length);
        for (var i = 0; i < run1.Length; i++)
        {
            Assert.Equal(run1[i].Index, run2[i].Index);
            Assert.Equal(run1[i].Score, run2[i].Score);
        }

        return;

        VectorSearchResult[] BuildAndSearch(int s)
        {
            var (_, index) = BuildRandomCorpusWithIndex(300, efConstruction: 200, seed: s);
            
            return index.Search(query, 10);
        }
    }
    
    #endregion

    #region Insert
    
    [Fact]
    public void Insert_WrongDimension_Throws()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);

        Assert.Throws<ArgumentException>(() => index.Insert(new float[Dimension + 1]));
    }
    
    [Fact]
    public void Insert_ProducesSymmetricEdges()
    {
        const int smallMaxConnections = 2;
        var random = new Random(Seed);
        var index = new MutableHnswIndex(
            dimension: Dimension,
            maxConnectionsLane: 4,
            maxConnectionsDense: smallMaxConnections,
            efConstruction: 20,
            seed: Seed
        );

        for (var i = 0; i < 100; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
        }

        var edgesList = ((MutableHnswIndex.DenseLayer)index.Layers[0])._edges;

        var asymmetricPairs = new List<(int From, int To)>();

        for (var nodeIndex = 0; nodeIndex < edgesList.Count; nodeIndex++)
        {
            var nodeEdges = edgesList[nodeIndex];
            if (nodeEdges == null) continue;

            foreach (var neighborIndex in nodeEdges)
            {
                var neighborEdges = neighborIndex < edgesList.Count ? edgesList[neighborIndex] : null;
                if (neighborEdges == null || !neighborEdges.Contains(nodeIndex))
                {
                    asymmetricPairs.Add((From: nodeIndex, To: neighborIndex));
                }
            }
        }

        Assert.Empty(asymmetricPairs);
    }

    #endregion

    #region Regression Tests
    
    [Fact]
    public void Search_Regression_RecallDoesNotDegrade()
    {
        var (corpus, index) = BuildRandomCorpusWithIndex(2000, efConstruction: 100);
        var random = new Random(Seed);

        var totalRecall = 0.0;
        for (var q = 0; q < 100; q++)
        {
            var query = GetTestVector(random, Dimension);
            var hnswResults = index.Search(query, 10, efSearch: 100);
            var bruteForceResults = BruteForceSearch(corpus, query, 10);
            var hnswIndices = new HashSet<int>(hnswResults.Select(r => r.Index));
            var hits = bruteForceResults.Count(hnswIndices.Contains);
            totalRecall += (double)hits / 10;
        }

        var averageRecall = totalRecall / 100.0;
        
        const double baselineRecall = 0.937;
        Assert.True(averageRecall >= baselineRecall, $"Recall regressed from {baselineRecall:P1} to {averageRecall:P1}");
    }

    #endregion

    #region Performance Benchmarks

    /// <summary>
    ///     Benchmarks insertion and search performance at various corpus sizes and efSearch values, and reports recall and timing.
    /// </summary>
    [Theory]
    [InlineData(500, 10)]
    [InlineData(1000, 10)]
    [InlineData(2000, 10)]
    [InlineData(5000, 10)]
    public void Performance_Benchmark(int vectorCount, int k)
    {
        const int efConstruction = 200;
        
        output.WriteLine($"Performance Test - vectors: {vectorCount}, k: {k}, efConstruction: {efConstruction}");
        
        var corpus = BuildRandomCorpusArray(vectorCount);

        #region Insertion Warmup

        var warmupIndex = new MutableHnswIndex(Dimension, 16, 32, efConstruction: efConstruction, seed: Seed);
        for (var i = 0; i <  Math.Min(250, vectorCount); i++)
        {
            warmupIndex.Insert(corpus[i]);
        }
        
        warmupIndex.Search(corpus[0], k, efSearch: 100);
        BruteForceSearch(corpus, corpus[0], k);

        #endregion

        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: efConstruction, seed: Seed);

        #region Insertion
        
        var insertSw = Stopwatch.StartNew();
        
        for (var i = 0; i < vectorCount; i++)
        {
            index.Insert(corpus[i]);
        }
        
        insertSw.Stop();

        output.WriteLine($"  Insert: {insertSw.Elapsed.TotalMilliseconds:F}ms total, {insertSw.Elapsed.TotalMilliseconds / vectorCount * 1000:F}µs/v");
        output.WriteLine($"  Layers: {index.Layers.Count}");
        
        #endregion
        
        var queries = GenerateRandomQueries(100);

        #region Search Warmup

        for (var i = 0; i < queries.Length; i++)
        {
            index.Search(queries[i], k, efSearch: 100);
        }

        #endregion
        
        output.WriteLine("  Search:");

        #region Search
        
        var efSearchValues = new[] { 50, 100, 200, 400 };
        foreach (var efSearch in efSearchValues)
        {
            if (efSearch < k)
            {
                continue;
            }

            var resultMatrix = new VectorSearchResult[queries.Length][];
            
            var searchSw = Stopwatch.StartNew();
            
            for (var q = 0; q < queries.Length; q++)
            {
                resultMatrix[q] = index.Search(queries[q], k, efSearch: efSearch);
            }
            
            searchSw.Stop();

            var totalRecall = 0.0;
            for (var q = 0; q < queries.Length; q++)
            {
                var bruteForceResults = BruteForceSearch(corpus, queries[q], k);
                var hnswIndices = new HashSet<int>(resultMatrix[q].Select(r => r.Index));
                var hits = bruteForceResults.Count(hnswIndices.Contains);
                totalRecall += (double)hits / k;
            }

            var avgRecall = totalRecall / queries.Length;
            var avgSearchMs = searchSw.Elapsed.TotalMilliseconds / queries.Length;

            output.WriteLine($"    ef: {efSearch,4}, recall: {avgRecall:P1}, search: {avgSearchMs * 1000:F1}µs/v");
        }
        
        #endregion
    }
    
    /// <summary>
    ///     Benchmarks brute force. Currently faster than HNSW due to the DS not being optimized.
    /// </summary>
    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2000)]
    [InlineData(5000)]
    public void Performance_BruteForceBaseline(int vectorCount)
    {
        var corpus = BuildRandomCorpusArray(vectorCount);
        var queries = GenerateRandomQueries(100);

        #region Warmup
        
        for (var i = 0; i < 1000; i++)
        {
            BruteForceSearch(corpus, queries[0], 10);
        }
        
        #endregion

        var searchSw = Stopwatch.StartNew();
        
        for (var q = 0; q < queries.Length; q++)
        {
            BruteForceSearch(corpus, queries[q], 10);
        }
        
        searchSw.Stop();

        var avgSearchMs = searchSw.Elapsed.TotalMilliseconds / queries.Length;
        output.WriteLine($"Brute force {vectorCount} vectors, k: 10, search: {avgSearchMs * 1000:F1}µs/v");
    }

    #endregion

    #region Allocator Tests

    [Fact]
    public void AllocationPage_AllocateBlock_ReturnsCorrectSize()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 10, pageCapacity: 5);
        var block = page.Allocate().Block;
        
        Assert.Equal(10, block.Length);
        Assert.Equal(1, page.SlotCount);
    }

    [Fact]
    public void AllocationPage_AllocateMultipleBlocks_ReturnsDistinctOrderedMemory()
    {
        unsafe
        {
            var page = new MutableHnswIndex.AllocationPage<float>(null!, pageIndex: 0, allocationLength: 4, pageCapacity: 3);
            var block1 = page.Allocate().Block;
            var block2 = page.Allocate().Block;

            using var h1 = block1.Pin();
            using var h2 = block2.Pin();
        
            Assert.False(h1.Pointer == h2.Pointer);
            Assert.True((float*)h2.Pointer == (float*)h1.Pointer + 4);
            Assert.Equal(2, page.SlotCount);
        }
    }

    [Fact]
    public void AllocationPage_IsFull_ReturnsTrueWhenFull()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 2, pageCapacity: 2);

        Assert.False(page.IsFull);
        page.Allocate();
        Assert.False(page.IsFull);
        page.Allocate();
        Assert.True(page.IsFull);
    }

    [Fact]
    public void AllocationPage_AllocateWhenFull_ThrowsInvalidOperationException()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 1, pageCapacity: 1);
        page.Allocate();

        Assert.Throws<InvalidOperationException>(() => page.Allocate());
    }

    [Fact]
    public void ArenaAllocator_AllocateBlock_UsesFirstPage()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<double>(allocationLength: 3, pageCapacity: 10);
        var block = allocator.Allocate().Block;

        Assert.Equal(3, block.Length);
        Assert.Single(allocator.Pages);
        Assert.Equal(1, allocator.Pages[0].SlotCount);
    }

    [Fact]
    public void ArenaAllocator_AllocateMultipleBlocks_ReusesPage()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<float>(allocationLength: 2, pageCapacity: 5);

        for (var i = 0; i < 5; i++)
        {
            allocator.Allocate();
        }

        Assert.Single(allocator.Pages);
        Assert.Equal(5, allocator.Pages[0].SlotCount);
        Assert.True(allocator.Pages[0].IsFull);
    }

    [Fact]
    public void ArenaAllocator_PageFull_CreatesNewPage()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 1, pageCapacity: 2);

        allocator.Allocate();
        allocator.Allocate();
        Assert.Single(allocator.Pages);

        allocator.Allocate();
        Assert.Equal(2, allocator.Pages.Length);
        Assert.Equal(1, allocator.Pages[1].SlotCount);
    }

    [Fact]
    public void ArenaAllocator_MultiplePages_UsesNonFullPage()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<long>(allocationLength: 1, pageCapacity: 2);

        allocator.Allocate();
        allocator.Allocate();
        allocator.Allocate();
        Assert.Equal(2, allocator.Pages.Length);

        allocator.Allocate();
        Assert.Equal(2, allocator.Pages.Length);
        Assert.Equal(2, allocator.Pages[1].SlotCount);
    }

    [Fact]
    public void ArenaAllocator_LargeAllocation_CreatesMultiplePages()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 10, pageCapacity: 5);
        const int totalAllocations = 17;

        for (var i = 0; i < totalAllocations; i++)
        {
            allocator.Allocate();
        }

        Assert.Equal(4, allocator.Pages.Length);
        Assert.True(allocator.Pages[0].IsFull);
        Assert.True(allocator.Pages[1].IsFull);
        Assert.True(allocator.Pages[2].IsFull);
        Assert.Equal(2, allocator.Pages[3].SlotCount);
    }

    [Fact]
    public void ArenaAllocator_BlockData_IsPersisted()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<float>(allocationLength: 4, pageCapacity: 3);
        
        var block1 = allocator.Allocate().Block;
        block1.Span[0] = 1.0f;
        block1.Span[1] = 2.0f;
        block1.Span[2] = 3.0f;
        block1.Span[3] = 4.0f;

        var block2 = allocator.Allocate().Block;
        block2.Span[0] = 5.0f;
        block2.Span[1] = 6.0f;
        block2.Span[2] = 7.0f;
        block2.Span[3] = 8.0f;

        Assert.Equal(1.0f, block1.Span[0]);
        Assert.Equal(2.0f, block1.Span[1]);
        Assert.Equal(3.0f, block1.Span[2]);
        Assert.Equal(4.0f, block1.Span[3]);

        Assert.Equal(5.0f, block2.Span[0]);
        Assert.Equal(6.0f, block2.Span[1]);
        Assert.Equal(7.0f, block2.Span[2]);
        Assert.Equal(8.0f, block2.Span[3]);
    }

    [Fact]
    public void ArenaAllocator_Deallocate_SlotIsReused()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 1, pageCapacity: 3);

        // ReSharper disable UnusedVariable
        var a = allocator.Allocate();
        var b = allocator.Allocate();
        var c = allocator.Allocate();
        // ReSharper restore UnusedVariable
        Assert.True(allocator.Pages[0].IsFull);

        allocator.Deallocate(in b);
        Assert.False(allocator.Pages[0].IsFull);
        Assert.True(allocator.Pages[0].HasFreeSlots);
        Assert.Contains(0, allocator.PagesWithReusedSlots);

        var d = allocator.Allocate();
        Assert.Equal(1, d.IndexInPage);
        Assert.True(allocator.Pages[0].IsFull);
        Assert.DoesNotContain(0, allocator.PagesWithReusedSlots);
        Assert.Single(allocator.Pages);
    }

    [Fact]
    public void ArenaAllocator_Deallocate_PrefersFreeSlotsOverNewPage()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 1, pageCapacity: 2);

        // ReSharper disable UnusedVariable
        var a = allocator.Allocate();
        var b = allocator.Allocate();
        // ReSharper restore UnusedVariable
        Assert.Single(allocator.Pages);

        allocator.Deallocate(in a);
        var c = allocator.Allocate();
        Assert.Equal(0, c.IndexInPage);
        Assert.Single(allocator.Pages);
    }

    [Fact]
    public void ArenaAllocator_Deallocate_TracksPagesWithFreeSlots()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 1, pageCapacity: 2);

        // ReSharper disable UnusedVariable
        var a = allocator.Allocate();
        var b = allocator.Allocate();
        var c = allocator.Allocate();
        // ReSharper restore UnusedVariable

        allocator.Deallocate(in a);
        Assert.Contains(0, allocator.PagesWithReusedSlots);

        allocator.Deallocate(in c);
        Assert.Contains(0, allocator.PagesWithReusedSlots);
        Assert.Contains(1, allocator.PagesWithReusedSlots);

        allocator.Allocate();
        Assert.DoesNotContain(0, allocator.PagesWithReusedSlots);
        allocator.Allocate();
        Assert.DoesNotContain(1, allocator.PagesWithReusedSlots);
    }

    [Fact]
    public void ArenaAllocator_Deallocate_MultipleFreesOnSamePage()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 1, pageCapacity: 3);

        // ReSharper disable UnusedVariable
        var a = allocator.Allocate();
        var b = allocator.Allocate();
        var c = allocator.Allocate();
        // ReSharper restore UnusedVariable

        allocator.Deallocate(in a);
        allocator.Deallocate(in c);

        Assert.Equal(2, allocator.Pages[0].FreeIndices.Count);

        var d = allocator.Allocate();
        Assert.Equal(2, d.IndexInPage);

        var e = allocator.Allocate();
        Assert.Equal(0, e.IndexInPage);
    }

    [Fact]
    public void AllocationPage_Deallocate_MakesFullPageNotFull()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 1, pageCapacity: 1);
        page.Allocate();
        Assert.True(page.IsFull);

        page.Deallocate(0);
        Assert.False(page.IsFull);
        Assert.True(page.HasFreeSlots);
    }

    [Fact]
    public void AllocationPage_Deallocate_DoubleFreeThrows()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 1, pageCapacity: 3);
        page.Allocate();
        page.Allocate();

        page.Deallocate(0);
        Assert.Throws<InvalidOperationException>(() => page.Deallocate(0));
    }

    [Fact]
    public void ArenaAllocator_Deallocate_DoubleFreeThrows()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 1, pageCapacity: 3);
        var a = allocator.Allocate();

        allocator.Deallocate(in a);
        Assert.Throws<InvalidOperationException>(() => allocator.Deallocate(in a));
    }

    [Fact]
    public void ArenaAllocator_Deallocate_ReusePreservesDataIntegrity()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 2, pageCapacity: 3);

        var a = allocator.Allocate();
        a.Block.Span[0] = 10;
        a.Block.Span[1] = 20;

        var b = allocator.Allocate();
        b.Block.Span[0] = 30;
        b.Block.Span[1] = 40;

        var c = allocator.Allocate();
        c.Block.Span[0] = 50;
        c.Block.Span[1] = 60;

        allocator.Deallocate(in b);

        var d = allocator.Allocate();
        Assert.Equal(1, d.IndexInPage);
        d.Block.Span[0] = 99;
        d.Block.Span[1] = 88;

        Assert.Equal(10, a.Block.Span[0]);
        Assert.Equal(20, a.Block.Span[1]);
        Assert.Equal(50, c.Block.Span[0]);
        Assert.Equal(60, c.Block.Span[1]);

        Assert.Equal(99, d.Block.Span[0]);
        Assert.Equal(88, d.Block.Span[1]);
    }

    [Fact]
    public void AllocationPage_Deallocate_UnallocatedIndexThrows()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 1, pageCapacity: 5);
        page.Allocate();
        page.Allocate();

        Assert.Throws<ArgumentOutOfRangeException>(() => page.Deallocate(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => page.Deallocate(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => page.Deallocate(-1));
    }

    [Fact]
    public void AllocationPage_Allocate_PrefersFreedSlotsOverBump()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 1, pageCapacity: 5);
        page.Allocate();
        page.Allocate();
        page.Allocate();

        page.Deallocate(1);

        var allocation = page.Allocate();
        Assert.Equal(1, allocation.IndexInPage);
        Assert.Equal(3, page.SlotCount);
    }

    [Fact]
    public void AllocationPage_AllSlotsFreedThenReallocated()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 1, pageCapacity: 3);
        page.Allocate();
        page.Allocate();
        page.Allocate();

        page.Deallocate(0);
        page.Deallocate(1);
        page.Deallocate(2);

        Assert.Equal(3, page.FreeIndices.Count);
        Assert.False(page.IsFull);

        page.Allocate();
        page.Allocate();
        page.Allocate();

        Assert.True(page.IsFull);
        Assert.Empty(page.FreeIndices);
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_CreatesBucketAndReturnsCorrectBlockSize()
    {
        var bucket = new MutableHnswIndex.BucketArenaAllocator<int>(basePageCapacity: 64, minPageSize: 4);
        var block = bucket.Allocate(5).Block;

        Assert.Equal(5, block.Length);
        Assert.Single(bucket.Allocators);
        Assert.True(bucket.Allocators.ContainsKey(5));
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_SameBucket_ReusesAllocator()
    {
        var bucket = new MutableHnswIndex.BucketArenaAllocator<float>(basePageCapacity: 64, minPageSize: 4);

        bucket.Allocate(3);
        bucket.Allocate(3);
        bucket.Allocate(3);

        Assert.Single(bucket.Allocators);
        Assert.Equal(3, bucket.Allocators[3].Pages[0].SlotCount);
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_DifferentLengths_CreatesSeparateBuckets()
    {
        var bucket = new MutableHnswIndex.BucketArenaAllocator<double>(basePageCapacity: 64, minPageSize: 4);

        bucket.Allocate(1);
        bucket.Allocate(2);
        bucket.Allocate(3);

        Assert.Equal(3, bucket.Allocators.Count);
        Assert.True(bucket.Allocators.ContainsKey(1));
        Assert.True(bucket.Allocators.ContainsKey(2));
        Assert.True(bucket.Allocators.ContainsKey(3));
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_PageCapacityScalesWithAllocationLength()
    {
        const int basePageCapacity = 64;
        const int minPageSize = 4;
        var bucket = new MutableHnswIndex.BucketArenaAllocator<int>(basePageCapacity, minPageSize);

        Assert.Equal(64, FillAndGetSlots(bucket, 1));
        Assert.Equal(32, FillAndGetSlots(bucket, 2));
        Assert.Equal(16, FillAndGetSlots(bucket, 4));
        Assert.Equal(8, FillAndGetSlots(bucket, 8));
        Assert.Equal(minPageSize, FillAndGetSlots(bucket, 16));
        return;

        static int FillAndGetSlots(MutableHnswIndex.BucketArenaAllocator<int> b, int length)
        {
            b.Allocate(length);
            var page = b.Allocators[length].Pages[0];
            while (!page.IsFull)
            {
                b.Allocate(length);
            }

            return page.SlotCount;
        }
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_MinPageSizeIsRespected()
    {
        const int minPageSize = 8;
        var bucket = new MutableHnswIndex.BucketArenaAllocator<int>(basePageCapacity: 16, minPageSize);

        bucket.Allocate(32);
        var page = bucket.Allocators[32].Pages[0];
        while (!page.IsFull)
        {
            bucket.Allocate(32);
        }

        Assert.Equal(minPageSize, page.SlotCount);
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_ZeroLength_ThrowsArgumentOutOfRangeException()
    {
        var bucket = new MutableHnswIndex.BucketArenaAllocator<int>(basePageCapacity: 64, minPageSize: 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => bucket.Allocate(0));
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_NegativeLength_ThrowsArgumentOutOfRangeException()
    {
        var bucket = new MutableHnswIndex.BucketArenaAllocator<int>(basePageCapacity: 64, minPageSize: 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => bucket.Allocate(-1));
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_DataIsPersistedAcrossBlocks()
    {
        var bucket = new MutableHnswIndex.BucketArenaAllocator<int>(basePageCapacity: 64, minPageSize: 4);

        var block1 = bucket.Allocate(3).Block;
        block1.Span[0] = 10;
        block1.Span[1] = 20;
        block1.Span[2] = 30;

        var block2= bucket.Allocate(5).Block;
        block2.Span[0] = 100;
        block2.Span[1] = 200;
        block2.Span[2] = 300;
        block2.Span[3] = 400;
        block2.Span[4] = 500;

        Assert.Equal(10, block1.Span[0]);
        Assert.Equal(20, block1.Span[1]);
        Assert.Equal(30, block1.Span[2]);

        Assert.Equal(100, block2.Span[0]);
        Assert.Equal(200, block2.Span[1]);
        Assert.Equal(300, block2.Span[2]);
        Assert.Equal(400, block2.Span[3]);
        Assert.Equal(500, block2.Span[4]);
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_ManyAllocations_CreatesMultiplePages()
    {
        const int basePageCapacity = 4;
        const int minPageSize = 2;
        var bucket = new MutableHnswIndex.BucketArenaAllocator<int>(basePageCapacity, minPageSize);

        for (var i = 0; i < 5; i++)
        {
            bucket.Allocate(1);
        }

        Assert.Equal(2, bucket.Allocators[1].Pages.Length);
        Assert.True(bucket.Allocators[1].Pages[0].IsFull);
        Assert.Equal(1, bucket.Allocators[1].Pages[1].SlotCount);
    }

    #endregion

    #region EdgeList Tests

    private static MutableHnswIndex.EdgeList CreateEdgeList(int capacity)
    {
        var storage = new int[capacity + 1];
        return new MutableHnswIndex.EdgeList(capacity, storage);
    }

    [Fact]
    public void EdgeList_Add_IncrementsCount()
    {
        var list = CreateEdgeList(4);
        Assert.Equal(0, list.Count);

        list.Add(10);
        Assert.Equal(1, list.Count);

        list.Add(20);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void EdgeList_Add_StoresValues()
    {
        var list = CreateEdgeList(4);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        Assert.Equal(10, list[0]);
        Assert.Equal(20, list[1]);
        Assert.Equal(30, list[2]);
    }

    [Fact]
    public void EdgeList_Add_FullThrows()
    {
        var list = CreateEdgeList(2);
        list.Add(1);
        list.Add(2);

        Assert.Throws<InvalidOperationException>(() => list.Add(3));
    }

    [Fact]
    public void EdgeList_Remove_ReturnsTrueWhenFound()
    {
        var list = CreateEdgeList(4);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        Assert.True(list.Remove(20));
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void EdgeList_Remove_ReturnsFalseWhenNotFound()
    {
        var list = CreateEdgeList(4);
        list.Add(10);
        list.Add(20);

        Assert.False(list.Remove(99));
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void EdgeList_Remove_SwapsLastElement()
    {
        var list = CreateEdgeList(4);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        list.Remove(10);

        Assert.Equal(30, list[0]);
        Assert.Equal(20, list[1]);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void EdgeList_Remove_LastElement_JustDecrements()
    {
        var list = CreateEdgeList(4);
        list.Add(10);
        list.Add(20);

        list.Remove(20);

        Assert.Equal(1, list.Count);
        Assert.Equal(10, list[0]);
    }

    [Fact]
    public void EdgeList_Remove_EmptyList_ReturnsFalse()
    {
        var list = CreateEdgeList(4);
        Assert.False(list.Remove(1));
    }

    #endregion
}