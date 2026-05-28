using KnowledgeSystem.Vector;
using Xunit.Abstractions;
using MutableHnswIndex = KnowledgeSystem.Vector.Hnsw.MutableHnswIndex;

// ReSharper disable LoopCanBeConvertedToQuery
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Tests;

public partial class MutableHnswIndexTests(ITestOutputHelper output)
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
        
        const double baselineRecall = 0.95;
        output.WriteLine($"Recall test: {averageRecall:P4} current, baseline: {baselineRecall:P4}");
        Assert.True(averageRecall >= baselineRecall, $"Recall regressed from {baselineRecall:P4} to {averageRecall:P4}");
    }
}