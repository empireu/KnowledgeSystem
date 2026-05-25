using System.Collections.Concurrent;
using System.Diagnostics;
using KnowledgeSystem.Vector;
using KnowledgeSystem.Vector.Hnsw;
using Xunit.Abstractions;
using MutableHnswIndex = KnowledgeSystem.Vector.Hnsw.MutableHnswIndex;

// ReSharper disable LoopCanBeConvertedToQuery
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
    ///     Builds a random orthonormal basis of size <paramref name="intrinsicDim"/> via Gram-Schmidt.
    /// </summary>
    private static float[][] BuildOrthonormalBasis(int intrinsicDim, int seed = Seed)
    {
        var random = new Random(seed);

        var basis = new float[intrinsicDim][];
        for (var b = 0; b < intrinsicDim; b++)
        {
            basis[b] = GetTestVector(random, Dimension);
        }

        for (var i = 1; i < intrinsicDim; i++)
        {
            for (var j = 0; j < i; j++)
            {
                var dot = 0.0f;
                for (var d = 0; d < Dimension; d++)
                {
                    dot += basis[i][d] * basis[j][d];
                }

                for (var d = 0; d < Dimension; d++)
                {
                    basis[i][d] -= dot * basis[j][d];
                }
            }

            var normSqr = 0.0f;
            for (var d = 0; d < Dimension; d++)
            {
                normSqr += basis[i][d] * basis[i][d];
            }

            var recipNorm = 1.0f / MathF.Sqrt(normSqr);
            for (var d = 0; d < Dimension; d++)
            {
                basis[i][d] *= recipNorm;
            }
        }

        return basis;
    }

    private static float[][] BuildClusteredCorpusArray(int count, float[][] basis, int seed = Seed)
    {
        var random = new Random(seed);
        var intrinsicDim = basis.Length;

        var corpus = new float[count][];
        for (var i = 0; i < count; i++)
        {
            var vector = new float[Dimension];
            for (var b = 0; b < intrinsicDim; b++)
            {
                var coefficient = (float)(random.NextDouble() * 2 - 1);
                for (var d = 0; d < Dimension; d++)
                {
                    vector[d] += coefficient * basis[b][d];
                }
            }

            var normSqr = 0.0f;
            for (var d = 0; d < Dimension; d++)
            {
                normSqr += vector[d] * vector[d];
            }

            if (normSqr > 1e-8f)
            {
                var k = 1.0f / MathF.Sqrt(normSqr);
                for (var d = 0; d < Dimension; d++)
                {
                    vector[d] *= k;
                }
            }

            corpus[i] = vector;
        }

        return corpus;
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
        .Select((v, i) => (Index: i, Score: VectorObjective.AdjustedCosineSimilarity(v, query)))
        .OrderBy(x => x.Score)
        .Take(k)
        .Select(x => x.Index)
        .ToArray();
    
    private static void AssertGraphIntegrity(MutableHnswIndex index)
    {
        var violations = new List<string>();

        // Check LayerCount matches the actual highest TargetLayer + 1:
        var actualMaxLayer = 0;
        for (var i = 0; i < index.VectorsInternal.Count; i++)
        {
            var node = index.VectorsInternal[i];
            if (node != null && node.TargetLayer >= actualMaxLayer)
            {
                actualMaxLayer = node.TargetLayer;
            }
        }

        if (index.LayerCount != actualMaxLayer + 1)
        {
            violations.Add($"LayerCount: {index.LayerCount} but actual max TargetLayer: {actualMaxLayer}");
        }

        // Check entry point exists and is on the highest layer:
        if (index.EntryPointVector == null)
        {
            var hasAnyNode = index.VectorsInternal.Any(v => v != null);
           
            if (hasAnyNode)
            {
                violations.Add("EntryPointVector is null but graph has nodes");
            }
        }
        else
        {
            if (index.EntryPointVector.TargetLayer != actualMaxLayer)
            {
                violations.Add($"EntryPointVector TargetLayer: {index.EntryPointVector.TargetLayer} but actual max TargetLayer: {actualMaxLayer}");
            }
        }

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

                    // Edge must not point out of range:
                    if (neighborIndex < 0 || neighborIndex >= index.VectorsInternal.Count)
                    {
                        violations.Add($"Node {nodeIndex} layer {layer} edge out of range {neighborIndex}");
                        continue;
                    }

                    // Edge must not point to a dead slot:
                    var neighbor = index.VectorsInternal[neighborIndex];
                
                    if (neighbor == null)
                    {
                        violations.Add($"Node {nodeIndex} layer {layer} edge dead slot {neighborIndex}");
                        continue;
                    }

                    // Neighbor must participate in this layer:
                    if (neighbor.TargetLayer < layer)
                    {
                        violations.Add($"Node {nodeIndex} layer {layer} edge node {neighborIndex} (TargetLayer: {neighbor.TargetLayer})");
                        continue;
                    }

                    // Edge must be symmetric:
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
                        violations.Add($"Node {nodeIndex} layer {layer} edge {neighborIndex} not reciprocated");
                    }
                }
            }
        }

        Assert.Empty(violations);
    }
    
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

    /// <summary>
    ///     Low dimensionality data.
    ///     Should achieve high recall, unlike full random data.
    /// </summary>
    [Theory]
    [InlineData(10000, 30, 10, 500)]
    [InlineData(10000, 80, 10, 500)]
    [InlineData(10000, 100, 10, 500)]
    public void Search_ClusteredData_AchievesHighRecall(int vectorCount, int intrinsicDim, int k, int queryCount)
    {
        // Build a shared basis so corpus and queries lie on the same low-dimensional manifold:
        var basis = BuildOrthonormalBasis(intrinsicDim);
        var corpus = BuildClusteredCorpusArray(vectorCount, basis);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        for (var i = 0; i < vectorCount; i++)
        {
            index.Insert(corpus[i]);
        }

        // Generate queries from the same low-dimensional manifold:
        var queryCorpus = BuildClusteredCorpusArray(queryCount, basis, seed: Seed + 77);
        var totalRecall = 0.0;

        var obj = new Lock();

        Parallel.For(0, queryCount, q =>
        {
            var query = queryCorpus[q];
            var hnswResults = index.Search(query, k, efSearch: 150);
            var bruteForceResults = BruteForceSearch(corpus, query, k);
            var hnswIndices = new HashSet<int>(hnswResults.Select(r => r.Index));
            var hits = bruteForceResults.Count(hnswIndices.Contains);

            lock (obj)
            {
                totalRecall += (double)hits / k;
            }
        });
        
        var averageRecall = totalRecall / queryCount;
        output.WriteLine($"LowD recall (intrinsicDim={intrinsicDim}): {averageRecall:P1}");
        Assert.True(averageRecall >= 0.95, $"Bad clustered recall {averageRecall:P1}");
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
    
    [Fact]
    public void Search_ExcludedIndices_NotInResults()
    {
        const int vectorCount = 200;
        var (_, index) = BuildRandomCorpusWithIndex(vectorCount, efConstruction: 200);
        var random = new Random(Seed);

        var query = GetTestVector(random, Dimension);
        var normalResults = index.Search(query, 10, efSearch: 200);

        var excluded = new HashSet<int>
        {
            normalResults[0].Index, 
            normalResults[1].Index,
            normalResults[2].Index
        };
        
        var excludedResults = index.Search(query, 10, efSearch: 200, predicate: i => !excluded.Contains(i));

        Assert.DoesNotContain(excludedResults, r => excluded.Contains(r.Index));
        Assert.Equal(10, excludedResults.Length);
    }

    [Fact]
    public void Search_ExcludedIndices_NullBehavesAsNormal()
    {
        const int vectorCount = 200;
        var (_, index) = BuildRandomCorpusWithIndex(vectorCount, efConstruction: 200);
        var random = new Random(Seed);

        var query = GetTestVector(random, Dimension);
        var normalResults = index.Search(query, 10, efSearch: 200);
        var nullExcludedResults = index.Search(query, 10, efSearch: 200, predicate: null);

        Assert.Equal(normalResults.Length, nullExcludedResults.Length);
        for (var i = 0; i < normalResults.Length; i++)
        {
            Assert.Equal(normalResults[i].Index, nullExcludedResults[i].Index);
        }
    }

    [Fact]
    public void Search_ExcludedIndices_PaginationReturnsDistinctResults()
    {
        const int vectorCount = 200;
        var (_, index) = BuildRandomCorpusWithIndex(vectorCount, efConstruction: 200);
        var random = new Random(Seed);

        var query = GetTestVector(random, Dimension);

        var page1 = index.Search(query, 5, efSearch: 200);
        var excluded = new HashSet<int>(page1.Select(r => r.Index));
        var page2 = index.Search(query, 5, efSearch: 200, predicate: i => !excluded.Contains(i));

        foreach (var r in page2)
        {
            excluded.Add(r.Index);
        }
        var page3 = index.Search(query, 5, efSearch: 200, predicate: i => !excluded.Contains(i));

        // All results across pages should be distinct:
        var allIndices = page1.Select(r => r.Index)
            .Concat(page2.Select(r => r.Index))
            .Concat(page3.Select(r => r.Index))
            .ToList();

        Assert.Equal(allIndices.Count, allIndices.Distinct().Count());

        // Results within each page should be sorted by score:
        for (var i = 1; i < page2.Length; i++)
        {
            Assert.True(page2[i].Score >= page2[i - 1].Score);
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

        var asymmetricPairs = new List<(int From, int To)>();

        for (var nodeIndex = 0; nodeIndex < index.VectorsInternal.Count; nodeIndex++)
        {
            var node = index.VectorsInternal[nodeIndex]!;

            var edgeCount = node.GetEdgesInLayer(0).Count;
            for (var i = 0; i < edgeCount; i++)
            {
                var neighborIndex = node.GetEdgesInLayer(0)[i];
                var neighbor = index.VectorsInternal[neighborIndex];
                var neighborEdgeCount = neighbor!.GetEdgesInLayer(0).Count;
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

    #endregion

    #region Regression Tests
    
    [Fact]
    public void Search_Regression_RecallDoesNotDegrade()
    {
        var (corpus, index) = BuildRandomCorpusWithIndex(2000, efConstruction: 100);
        var random = new Random(Seed);

        var totalRecall = 0.0;
        for (var q = 0; q < 10000; q++)
        {
            var query = GetTestVector(random, Dimension);
            var hnswResults = index.Search(query, 10, efSearch: 100);
            var bruteForceResults = BruteForceSearch(corpus, query, 10);
            var hnswIndices = new HashSet<int>(hnswResults.Select(r => r.Index));
            var hits = bruteForceResults.Count(hnswIndices.Contains);
            totalRecall += (double)hits / 10;
        }

        var averageRecall = totalRecall / 10000.0;
        
        const double baselineRecall = 0.9507;
        output.WriteLine($"Recall test: {averageRecall:P4} current, baseline: {baselineRecall:P4}");
        Assert.True(averageRecall >= baselineRecall, $"Recall regressed from {baselineRecall:P4} to {averageRecall:P4}");
    }

    #endregion

    #region Performance Benchmarks

    /// <summary>
    ///     Benchmarks insertion and search performance at various corpus sizes and efSearch values, and reports recall and timing.
    /// </summary>
    [Theory]
    [InlineData(2500, 10)]
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
        output.WriteLine($"  Layers: {index.LayerCount}");
        
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
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(capacity + 1, 4);
        return new MutableHnswIndex.EdgeList(capacity, allocator.Allocate());
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

    #region Removal

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
        var entryPointIndex = index.VectorsInternal.First(v => v != null && v.TargetLayer == index.LayerCount - 1)!.Index;
        var entryPoint = index.Vectors[entryPointIndex]!;

        Assert.True(index.Remove(entryPoint));

        // Search should still work:
        var results = index.Search(corpus[0], 5);
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
        var topLayerVectors = vectors.Where(v => index.VectorsInternal[v.Index]?.TargetLayer == initialLayerCount - 1).ToList();
        foreach (var v in topLayerVectors)
        {
            index.Remove(v);
        }

        Assert.True(index.LayerCount < initialLayerCount, "LayerCount should decrease after removing all top-layer vectors");
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

    #endregion

    #region Saving

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
     
        Assert.True(Math.Abs(avgRecallOriginal - avgRecallLoaded) < 1e-5, $"Recall mismatch: original {avgRecallOriginal:P1}, loaded {avgRecallLoaded:P1}");
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

    #endregion

    #region Concurrent Insert

    [Theory]
    [InlineData(500, 4)]
    [InlineData(1000, 8)]
    [InlineData(2000, 4)]
    public void ConcurrentInsert_ProducesCorrectVectorCount(int vectorCount, int parallelism)
    {
        var corpus = BuildRandomCorpusArray(vectorCount);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        Parallel.For(0, vectorCount, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, i =>
        {
            index.Insert(corpus[i]);
        });

        var liveCount = 0;
        for (var i = 0; i < index.VectorsInternal.Count; i++)
        {
            if (index.VectorsInternal[i] != null)
            {
                liveCount++;
            }
        }

        Assert.Equal(vectorCount, liveCount);
    }

    [Theory]
    [InlineData(500, 4)]
    [InlineData(1000, 8)]
    public void ConcurrentInsert_GraphIntegrity(int vectorCount, int parallelism)
    {
        var corpus = BuildRandomCorpusArray(vectorCount);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        Parallel.For(0, vectorCount, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, i =>
        {
            index.Insert(corpus[i]);
        });

        AssertGraphIntegrity(index);
    }

    [Fact]
    public void ConcurrentInsert_RecallIsAcceptable()
    {
        for (var repeat = 0; repeat < 10; repeat++)
        {
            const int vectorCount = 1000;
            const int k = 10;
            const int queryCount = 500;

            var corpus = BuildRandomCorpusArray(vectorCount);
            var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

            // Track the actual stored vector index for each corpus entry:
            var storedVectors = new IStoredVector[vectorCount];

            Parallel.For(0, vectorCount, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            {
                storedVectors[i] = index.Insert(corpus[i]);
            });
            
            AssertGraphIntegrity(index);

            // Build a lookup from HNSW index to corpus data for brute-force comparison:
            var indexToCorpus = new Dictionary<int, float[]>();
            for (var i = 0; i < vectorCount; i++)
            {
                indexToCorpus[storedVectors[i].Index] = corpus[i];
            }

            var random = new Random(Seed + 99);
            var totalRecall = 0.0;
            for (var q = 0; q < queryCount; q++)
            {
                var query = GetTestVector(random, Dimension);
                var hnswResults = index.Search(query, k, efSearch: 200);

                // Brute-force: compute distance from query to every stored vector, sort, take k:
                var bruteForceResults = indexToCorpus
                    .Select(kv => (Index: kv.Key, Score: VectorObjective.AdjustedCosineSimilarity(kv.Value, query)))
                    .OrderBy(x => x.Score)
                    .Take(k)
                    .Select(x => x.Index)
                    .ToHashSet();

                var hnswIndices = new HashSet<int>(hnswResults.Select(r => r.Index));
                var hits = hnswIndices.Count(bruteForceResults.Contains);
                totalRecall += (double)hits / k;
            }

            var averageRecall = totalRecall / queryCount;
            output.WriteLine($"Concurrent insert recall: {averageRecall:P1}");

            // Concurrent build may have slightly lower recall than sequential due to insertion order effects.
            Assert.True(averageRecall >= 0.75, $"Bad average recall after concurrent insert: {averageRecall:P1}");   
        }
    }

    [Fact]
    public async Task ConcurrentInsert_NoDeadlock()
    {
        const int vectorCount = 3000;
        var corpus = BuildRandomCorpusArray(vectorCount);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        var completed = false;
        var task = Task.Run(() =>
        {
            Parallel.For(0, vectorCount, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                index.Insert(corpus[i]);
            });
            
            completed = true;
        });

        await task.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(completed, "Concurrent insert did not complete");
    }

    [Fact]
    public async Task ConcurrentInsert_WhileSearching_DoesNotCrash()
    {
        const int vectorCount = 1000;
        var corpus = BuildRandomCorpusArray(vectorCount);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        // Insert an initial batch so search has something to work with:
        for (var i = 0; i < 50; i++)
        {
            index.Insert(corpus[i]);
        }

        var searchExceptions = new ConcurrentBag<Exception>();
        var cts = new CancellationTokenSource();

        // Start a background search loop:
        var searchTask = Task.Run(() =>
        {
            var random = new Random(Seed + 42);
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var query = GetTestVector(random, Dimension);
                    index.Search(query, 5, efSearch: 50);
                }
                catch (Exception ex)
                {
                    searchExceptions.Add(ex);
                }
            }
        }, cts.Token);

        // Insert remaining vectors in parallel:
        Parallel.For(50, vectorCount, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            index.Insert(corpus[i]);
        });

        await cts.CancelAsync();
        // ReSharper disable once MethodSupportsCancellation
        await searchTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(searchExceptions);
    }

    /// <summary>
    ///     High-contention test: small MaxConnections forces frequent TrimEdges,
    ///     widening the race window for stale snapshot reads.
    /// </summary>
    [Theory]
    [InlineData(500, 4, 8)]
    [InlineData(1000, 4, 12)]
    public async Task ConcurrentInsert_SmallMaxConnections_NoCrash(int vectorCount, int laneConnections, int denseConnections)
    {
        var corpus = BuildRandomCorpusArray(vectorCount);
        var index = new MutableHnswIndex(Dimension, laneConnections, denseConnections, efConstruction: 50, seed: Seed);

        var searchExceptions = new ConcurrentBag<Exception>();
        var cts = new CancellationTokenSource();

        // Insert an initial batch so search has something to work with:
        for (var i = 0; i < 20; i++)
        {
            index.Insert(corpus[i]);
        }

        // Start a background search loop with tiny efSearch to stress stale-edge sensitivity:
        var searchTask = Task.Run(() =>
        {
            var random = new Random(Seed + 42);
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var query = GetTestVector(random, Dimension);
                    index.Search(query, 5, efSearch: 2);
                }
                catch (Exception ex)
                {
                    searchExceptions.Add(ex);
                }
            }
        }, cts.Token);

        // Insert remaining vectors in parallel:
        Parallel.For(20, vectorCount, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            index.Insert(corpus[i]);
        });

        await cts.CancelAsync();
        
        // ReSharper disable once MethodSupportsCancellation
        await searchTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(searchExceptions);
    }

    /// <summary>
    ///     Time-based stress test: runs concurrent insert + search for a fixed duration.
    ///     Uses small MaxConnections to maximize TrimEdges frequency and Thread.Start
    ///     for precise concurrent startup. More effective than iteration-based tests
    ///     at surfacing rare sparse-layer races.
    /// </summary>
    [Fact]
    public void ConcurrentInsert_WhileSearching_TimeBased_NoCrash()
    {
        const int durationSeconds = 10;
        const int threadCount = 4;
        var index = new MutableHnswIndex(Dimension, 4, 8, efConstruction: 50, seed: Seed);
        var exceptions = new ConcurrentBag<Exception>();
        var cts = new CancellationTokenSource();

        // Seed the index with a few vectors so search has something to traverse:
        var seedRandom = new Random(Seed);
        for (var i = 0; i < 30; i++)
        {
            index.Insert(GetTestVector(seedRandom, Dimension));
        }

        // Insert threads: continuously insert new vectors:
        var insertThreads = new Thread[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            var random = new Random(Seed + t);
            insertThreads[t] = new Thread(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        index.Insert(GetTestVector(random, Dimension));
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            })
            {
                IsBackground = true
            };
        }

        // Search threads: continuously search with tiny efSearch:
        var searchThreads = new Thread[2];
        for (var t = 0; t < searchThreads.Length; t++)
        {
            var random = new Random(Seed + 100 + t);
            searchThreads[t] = new Thread(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var query = GetTestVector(random, Dimension);
                        index.Search(query, 5, efSearch: 2);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            })
            {
                IsBackground = true
            };
        }

        // Start all threads simultaneously:
        foreach (var thread in insertThreads) thread.Start();
        foreach (var thread in searchThreads) thread.Start();

        // Let them hammer the index:
        Thread.Sleep(TimeSpan.FromSeconds(durationSeconds));
        cts.Cancel();

        foreach (var thread in insertThreads) thread.Join(TimeSpan.FromSeconds(5));
        foreach (var thread in searchThreads) thread.Join(TimeSpan.FromSeconds(5));

        Assert.Empty(exceptions);
    }

    #endregion
}