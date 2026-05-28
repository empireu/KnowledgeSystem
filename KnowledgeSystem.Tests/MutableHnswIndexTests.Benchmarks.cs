using System.Diagnostics;
using KnowledgeSystem.Vector;
using KnowledgeSystem.Vector.Hnsw;

namespace KnowledgeSystem.Tests;

public partial class MutableHnswIndexTests
{
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
        for (var i = 0; i < Math.Min(250, vectorCount); i++)
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

        output.WriteLine(
            $"  Insert: {insertSw.Elapsed.TotalMilliseconds:F}ms total, {insertSw.Elapsed.TotalMilliseconds / vectorCount * 1000:F}µs/v");
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
}