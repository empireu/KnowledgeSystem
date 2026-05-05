using System.Numerics.Tensors;
using KnowledgeSystem.VectorDatabase;

namespace KnowledgeSystem.Tests;

public class HnsIndexTests
{
    private const int Seed = 3141;
    private const int Dimension = 512;
    
    /// <summary>
    ///     Brute-force search for the exact K best vectors.
    /// </summary>
    private static int[] BruteForceSearch(float[][] corpus, float[] query, int k) => corpus
        .Select((v, i) => (Index: i, Score: 1.0f - TensorPrimitives.CosineSimilarity(v, query)))
        .OrderBy(x => x.Score)
        .Take(k)
        .Select(x => x.Index)
        .ToArray();
    
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
        var random = new Random(Seed);
        var index = new MutableHnswIndex(
            dimension: Dimension,
            maxConnectionsLane: 16,
            maxConnectionsDense: 32,
            efConstruction: 200,
            seed: Seed
        );

        // Build the corpus:
        var corpus = new float[vectorCount][];
        for (var i = 0; i < vectorCount; i++)
        {
            corpus[i] = GetTestVector(random, Dimension);
            index.Insert(corpus[i]);
        }

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
        var random = new Random(Seed);
        const int corpusSize = 10;
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);

        for (var i = 0; i < corpusSize; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
        }

        var query = GetTestVector(random, Dimension);
        var results = index.Search(query, 100);

        Assert.Equal(corpusSize, results.Length);
        Assert.Equal(corpusSize, results.Select(r => r.Index).Distinct().Count());
    }

    [Fact]
    public void Search_ExactMatch_IsTopResult()
    {
        var random = new Random(Seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        var corpus = new float[100][];
        for (var i = 0; i < 100; i++)
        {
            corpus[i] = GetTestVector(random, Dimension);
            index.Insert(corpus[i]);
        }

        const int targetIndex = 50;
        var results = index.Search(corpus[targetIndex], 5);

        Assert.Equal(targetIndex, results[0].Index);
        Assert.True(results[0].Score < 1e-5f, $"Error in the self score, got {results[0].Score}");
    }

    [Fact]
    public void Search_ResultsAreSortedByScoreAscending()
    {
        var random = new Random(Seed);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        for (var i = 0; i < 200; i++)
        {
            index.Insert(GetTestVector(random, Dimension));
        }

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
    public void Insert_WrongDimension_Throws()
    {
        var index = new MutableHnswIndex(Dimension, 16, 32, seed: Seed);

        Assert.Throws<ArgumentException>(() => index.Insert(new float[Dimension + 1]));
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

        VectorSearchResult[] BuildAndSearch(int s)
        {
            var random = new Random(s);
            var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: s);
            for (var i = 0; i < 300; i++)
            {
                index.Insert(GetTestVector(random, Dimension));
            }
            
            return index.Search(query, 10);
        }

        var run1 = BuildAndSearch(123);
        var run2 = BuildAndSearch(123);

        Assert.Equal(run1.Length, run2.Length);
        for (var i = 0; i < run1.Length; i++)
        {
            Assert.Equal(run1[i].Index, run2[i].Index);
            Assert.Equal(run1[i].Score, run2[i].Score);
        }
    }

    private static float[] GetTestVector(Random random, int dimension, bool normalized = true)
    {
        var vector = new float[dimension];
        var normSqr = 0.0f;
        for (var i = 0; i < dimension; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2 - 1);
            normSqr += vector[i] * vector[i];
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
}