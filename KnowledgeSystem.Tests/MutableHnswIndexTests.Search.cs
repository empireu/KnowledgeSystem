using KnowledgeSystem.Vector;
using KnowledgeSystem.Vector.Hnsw;

namespace KnowledgeSystem.Tests;

public partial class MutableHnswIndexTests
{
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
}