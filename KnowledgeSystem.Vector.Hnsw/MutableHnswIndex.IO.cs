using System.Text;

// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Vector.Hnsw;

public sealed partial class MutableHnswIndex
{
    private const ushort CurrentFormatVersion = 1;

    /// <summary>
    ///     Serializes the index to a stream.
    ///     NOT thread-safe. Must not be called concurrently with <see cref="Insert"/>, <see cref="Remove"/>, or other IO operations.
    /// </summary>
    public void SaveToFile(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write((byte)'H');
        writer.Write((byte)'N');
        writer.Write((byte)'S');
        writer.Write((byte)'W');
        writer.Write(CurrentFormatVersion);
        writer.Write(Dimension);
        writer.Write(MaxConnectionsLane);
        writer.Write(MaxConnectionsDense);
        writer.Write(ExplorationFactorConstruction);
        writer.Write(LayerCount);
        writer.Write(EntryPointVector?.Index ?? -1);
        writer.Write(VectorsInternal.Count);

        // Free slots:
        var freeSlots = _freeSlots.ToArray();
        writer.Write(freeSlots.Length);
        for (var i = 0; i < freeSlots.Length; i++)
        {
            writer.Write(freeSlots[i]);
        }

        // Vectors:
        for (var i = 0; i < VectorsInternal.Count; i++)
        {
            var vector = VectorsInternal[i];
            writer.Write(vector != null ? (byte)1 : (byte)0);

            if (vector == null)
            {
                continue;
            }

            writer.Write(vector.TargetLayer);

            // Vector data:
            var span = vector.VectorStorage.Block.Span;
            for (var j = 0; j < Dimension; j++)
            {
                writer.Write(span[j]);
            }

            // Dense edges:
            WriteEdgeList(writer, vector.DenseGraph);

            // Sparse edges:
            for (var layer = 1; layer <= vector.TargetLayer; layer++)
            {
                WriteEdgeList(writer, vector.GetEdgesInLayer(layer));
            }
        }
    }
    
    public static MutableHnswIndex LoadFromFile(string path,
        int vectorPageSize = 512,
        int sparseEdgePageSize = 512 * 512,
        int denseEdgePageSize = 256 * 512,
        int baseLayerArrayPageCapacity = 512 * 512,
        int minLayerArrayPageSize = 32 * 512,
        int? seed= null)
    {
        using var stream = File.OpenRead(path);
        return Load(stream, vectorPageSize, sparseEdgePageSize, denseEdgePageSize, baseLayerArrayPageCapacity, minLayerArrayPageSize, seed);
    }
    
    public void Save(string path)
    {
        using var stream = File.Create(path);
        SaveToFile(stream);
    }

    /// <summary>
    ///     Loads an index from a stream.
    /// </summary>
    public static MutableHnswIndex Load(Stream stream,
        int vectorPageSize = 512,
        int sparseEdgePageSize = 512 * 512,
        int denseEdgePageSize = 256 * 512,
        int baseLayerArrayPageCapacity = 512 * 512,
        int minLayerArrayPageSize = 32 * 512,
        int? seed= null)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (reader.ReadByte() != 'H' || reader.ReadByte() != 'N' || reader.ReadByte() != 'S' || reader.ReadByte() != 'W')
        {
            throw new InvalidDataException($"Invalid file header");
        }

        var version = reader.ReadUInt16();
        if (version != CurrentFormatVersion)
        {
            throw new InvalidDataException($"Invalid format version {version}");
        }

        var dimension = reader.ReadInt32();
        var maxConnectionsLane = reader.ReadInt32();
        var maxConnectionsDense = reader.ReadInt32();
        var efConstruction = reader.ReadInt32();
        var layerCount = reader.ReadInt32();
        var entryPointIndex = reader.ReadInt32();
        var vectorCount = reader.ReadInt32();

        var index = new MutableHnswIndex(
            dimension, maxConnectionsLane, maxConnectionsDense, efConstruction,
            seed: seed,
            vectorPageSize, sparseEdgePageSize, denseEdgePageSize,
            baseLayerArrayPageCapacity, minLayerArrayPageSize
        );

        // Free slots:
        var freeSlotCount = reader.ReadInt32();
        var freeSlots = new int[freeSlotCount];
        for (var i = 0; i < freeSlotCount; i++)
        {
            freeSlots[i] = reader.ReadInt32();
        }

        // Pre-size the vectors list with nulls:
        for (var i = 0; i < vectorCount; i++)
        {
            index.VectorsInternal.Add(null);
        }

        // Read and allocate vectors
        for (var i = 0; i < vectorCount; i++)
        {
            var isPresent = reader.ReadByte();
            
            if (isPresent == 0)
            {
                continue;
            }

            var targetLayer = reader.ReadInt32();

            // Vector data:
            var vectorData = new float[dimension];
            for (var j = 0; j < dimension; j++)
            {
                vectorData[j] = reader.ReadSingle();
            }

            // Dense edges:
            var denseEdges = ReadEdgeList(reader);

            // Sparse edges:
            var sparseEdgesPerLayer = new int[targetLayer][];
            for (var layer = 1; layer <= targetLayer; layer++)
            {
                sparseEdgesPerLayer[layer - 1] = ReadEdgeList(reader);
            }

            // Allocate the vector with the known target layer and index:
            var vector = index.AllocatePreDefinedVector(vectorData, targetLayer, i);

            // Populate dense edges:
            for (var j = 0; j < denseEdges.Length; j++)
            {
                vector.DenseGraph.Add(denseEdges[j]);
            }

            // Populate sparse edges:
            for (var layer = 1; layer <= targetLayer; layer++)
            {
                var sparseGraph = vector.GetEdgesInLayer(layer);
                var edges = sparseEdgesPerLayer[layer - 1];
                for (var j = 0; j < edges.Length; j++)
                {
                    sparseGraph.Add(edges[j]);
                }
            }
        }

        // Restore entry point:
        if (entryPointIndex >= 0)
        {
            var entryPoint = index.VectorsInternal[entryPointIndex];
            
            if (entryPoint == null)
            {
                throw new InvalidDataException($"Entry point index {entryPointIndex} points to a null slot");
            }

            index.EntryPointVector = entryPoint;
        }

        // Restore layer count:
        index.LayerCount = layerCount;

        // Restore free slots in correct order:
        for (var i = freeSlotCount - 1; i >= 0; i--)
        {
            index._freeSlots.Push(freeSlots[i]);
        }

        return index;
    }

    private static void WriteEdgeList(BinaryWriter writer, EdgeList edges)
    {
        writer.Write(edges.Count);
        for (var i = 0; i < edges.Count; i++)
        {
            writer.Write(edges[i]);
        }
    }

    private static int[] ReadEdgeList(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var edges = new int[count];
        for (var i = 0; i < count; i++)
        {
            edges[i] = reader.ReadInt32();
        }

        return edges;
    }

    /// <summary>
    ///     Allocates a vector with a known target layer and index for deserializing.
    ///     Unlike <see cref="AllocateVector"/>, this does not roll a random layer and does not use free slots.
    /// </summary>
    private StoredVectorImpl AllocatePreDefinedVector(float[] sourceData, int targetLayer, int index)
    {
        var vectorStorage = _vectorAllocator.Allocate();

        var denseGraph = new EdgeList(MaxConnectionsDense + 1, _denseEdgeStorageAllocator.Allocate());
        denseGraph.Clear();

        Allocation<EdgeList>? sparseGraphs = targetLayer > 0
            ? _layerStorageAllocator.Allocate(targetLayer)
            : null;

        if (sparseGraphs.HasValue)
        {
            var array = sparseGraphs.Value.Block.Span;
           
            for (var i = 0; i < array.Length; i++)
            {
                var sparseList = new EdgeList(MaxConnectionsLane + 1, _laneEdgeStorageAllocator.Allocate());
                sparseList.Clear();
                array[i] = sparseList;
            }
        }

        var result = new StoredVectorImpl(index, vectorStorage, denseGraph, sparseGraphs);
        VectorsInternal[index] = result;
        result.Load(sourceData);

        return result;
    }
}
