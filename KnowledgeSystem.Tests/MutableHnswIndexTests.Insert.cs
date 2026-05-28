using KnowledgeSystem.Vector.Hnsw;

namespace KnowledgeSystem.Tests;

public partial class MutableHnswIndexTests
{
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
}