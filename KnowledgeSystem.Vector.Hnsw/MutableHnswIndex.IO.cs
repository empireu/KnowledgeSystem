using System.Runtime.CompilerServices;
using System.Text;
// ReSharper disable LoopCanBeConvertedToQuery

// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Vector.Hnsw;

public sealed partial class MutableHnswIndex
{
    private const int BufferSize = 1024 * 1024;
    private const ushort LegacyFormatVersion = 1;
    private const ushort CurrentFormatVersion = 2;

    /// <summary>
    ///     Fixed header size in bytes for format version 2 (fixed-slot).
    /// </summary>
    private const int HeaderSize = 56;

    /// <summary>
    ///     Computes the fixed size of one vector slot.
    /// </summary>
    private int SlotSize => 1 + 4 + Dimension * 4 + 4 + MaxConnectionsDense * 4 + (MaxLayersAllocated - 1) * (4 + MaxConnectionsLane * 4);

    /// <summary>
    ///     Computes the file offset for the slot at <paramref name="slotIndex"/>.
    /// </summary>
    private long SlotOffset(int slotIndex) => HeaderSize + (long)slotIndex * SlotSize;

    /// <summary>
    ///     Marks a vector slot as dirty so it will be written on the next incremental save.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MarkVectorDirty(int vectorIndex)
    {
        lock (_dirtyLock)
        {
            _dirtyVectorIndices.Add(vectorIndex);
        }
    }

    /// <summary>
    ///     Marks that header fields (EntryPointVector, LayerCount, free slots) have changed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MarkStructuralChange()
    {
        lock (_dirtyLock) // Consistency; basically no cost here.
        {
            _hasStructuralChanges = true;
        }
    }

    public static MutableHnswIndex LoadFromFile(string path,
        int vectorPageSize = 512,
        int sparseEdgePageSize = 512 * 512,
        int denseEdgePageSize = 256 * 512,
        int baseLayerArrayPageCapacity = 512 * 512,
        int minLayerArrayPageSize = 32 * 512,
        int? seed = null)
    {
        using var fileStream = File.OpenRead(path);
        using var stream = new BufferedStream(fileStream, BufferSize);
        return Load(stream, vectorPageSize, sparseEdgePageSize, denseEdgePageSize, baseLayerArrayPageCapacity, minLayerArrayPageSize, seed);
    }

    public void Save(string path)
    {
        using var fileStream = File.Open(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        fileStream.SetLength(0);
        using var stream = new BufferedStream(fileStream, BufferSize);
        SaveToFile(stream);
    }
    
    #region Save

    /// <summary>
    ///     Saves only the changed (dirty) slots to the file, plus the header if structural changes occurred.
    ///     Thread-safe. Acquires <see cref="_saveSemaphore"/> to serialize saves, and briefly takes <see cref="_graphLock"/> per vector to snapshot edge data.
    ///     If any vector's <see cref="StoredVectorImpl.TargetLayer"/> exceeds <see cref="MaxLayersAllocated"/>, it will result into a <see cref="RewriteFile"/>.
    /// </summary>
    public void SaveToFile(Stream stream)
    {
        _saveSemaphore.Wait();
        
        try
        {
            stream = new BufferedStream(stream, BufferSize);

            // If the stream is empty (first save), always do a full rewrite:
            var isFirstSave = stream is { CanSeek: true, Length: 0 };

            // Snapshot the dirty state while holding the graph lock so we do not observe a structural header change without the matching slot update:
            List<int> dirtySnapshot;
            bool structuralSnapshot;
            int vectorCountSnapshot;
            bool needsRewrite;

            lock (_graphLock)
            {
                lock (_dirtyLock)
                {
                    dirtySnapshot = [.._dirtyVectorIndices];
                    _dirtyVectorIndices.Clear();
                    structuralSnapshot = _hasStructuralChanges;
                    _hasStructuralChanges = false;
                }

                if (dirtySnapshot.Count == 0 && !structuralSnapshot && !isFirstSave)
                {
                    return;
                }

                // Check whether any dirty vector exceeds MaxLayersAllocated:
                needsRewrite = isFirstSave;
                for (var index = 0; index < dirtySnapshot.Count; index++)
                {
                    var vectorIndex = dirtySnapshot[index];
                    if (vectorIndex >= VectorsInternal.Count)
                    {
                        continue;
                    }

                    var vector = VectorsInternal[vectorIndex];
                  
                    if (vector != null && vector.TargetLayer >= MaxLayersAllocated)
                    {
                        needsRewrite = true;
                        break;
                    }
                }

                vectorCountSnapshot = VectorsInternal.Count;
            }

            if (needsRewrite)
            {
                RewriteFile(stream);
                return;
            }

            // Check if the file needs to be inflated:
            if (stream.CanSeek)
            {
                var currentSlotCount = (int)((stream.Length - HeaderSize) / SlotSize);
                var neededSlotCount = vectorCountSnapshot;

                if (neededSlotCount > currentSlotCount)
                {
                    // Extend the file with dead slots for all new positions:
                    stream.Position = SlotOffset(currentSlotCount);
                    for (var slotIndex = currentSlotCount; slotIndex < neededSlotCount; slotIndex++)
                    {
                        WriteSlot(stream, in SlotSnapshot.Dead);
                    }

                    // Assign FileOffset to all new vectors that don't have one yet:
                    lock (_graphLock)
                    {
                        for (var i = currentSlotCount; i < neededSlotCount; i++)
                        {
                            var vector = VectorsInternal[i];
                            
                            if (vector is { FileOffset: 0 })
                            {
                                vector.FileOffset = SlotOffset(i);
                            }
                        }
                    }

                    // Header must be updated with the new VectorSlotCount:
                    structuralSnapshot = true;
                }
            }

            // Write header if structural changes (or file was just extended):
            if (structuralSnapshot)
            {
                lock (_allocationLock)
                {
                    lock (_graphLock)
                    {
                        WriteHeader(stream);
                    }
                }
            }

            for (var i = 0; i < dirtySnapshot.Count; i++)
            {
                var vectorIndex = dirtySnapshot[i];

                SlotSnapshot snapshot;
                lock (_graphLock)
                {
                    if (vectorIndex >= VectorsInternal.Count)
                    {
                        continue;
                    }

                    var vector = VectorsInternal[vectorIndex];
                    
                    snapshot = vector != null 
                        ? SnapshotVector(vector)
                        : SlotSnapshot.Dead;
                }

                stream.Position = SlotOffset(vectorIndex);
                WriteSlot(stream, in snapshot);
            }
        }
        finally
        {
            stream.Flush();
            _saveSemaphore.Release();
        }
    }

    /// <summary>
    ///     Full rewrite of the file. Takes <see cref="_graphLock"/> and <see cref="_allocationLock"/> exclusively to get a consistent snapshot, then writes header and all slots.
    ///     Updates <see cref="MaxLayersAllocated"/> and all <see cref="StoredVectorImpl.FileOffset"/> values.
    /// </summary>
    private void RewriteFile(Stream stream)
    {
        lock (_allocationLock)
        {
            lock (_graphLock)
            {
                // Determine new MaxLayersAllocated. Could be done faster, but it's fine:
                var maxTargetLayer = 0;
                for (var i = 0; i < VectorsInternal.Count; i++)
                {
                    var vector = VectorsInternal[i];
                    
                    if (vector != null && vector.TargetLayer > maxTargetLayer)
                    {
                        maxTargetLayer = vector.TargetLayer;
                    }
                }

                // Some padding so we don't need to rewrite on every new layer:
                MaxLayersAllocated = Math.Max(maxTargetLayer + 2, 1);

                // Assign FileOffset to every vector:
                for (var i = 0; i < VectorsInternal.Count; i++)
                {
                    var vector = VectorsInternal[i];
                    
                    vector?.FileOffset = SlotOffset(i);
                }

                // Write header:
                WriteHeader(stream);

                // Write all slots:
                for (var i = 0; i < VectorsInternal.Count; i++)
                {
                    var vector = VectorsInternal[i];
                    
                    var snapshot = vector != null 
                        ? SnapshotVector(vector)
                        : SlotSnapshot.Dead;
                    
                    stream.Position = SlotOffset(i);
                    WriteSlot(stream, in snapshot);
                }

                // Truncate file to exact size:
                if (stream.CanSeek)
                {
                    stream.SetLength(SlotOffset(VectorsInternal.Count));
                }

                lock (_dirtyLock)
                {
                    _dirtyVectorIndices.Clear();
                    _hasStructuralChanges = false;
                }
            }
        }
    }

    /// <summary>
    ///     Writes the fixed-size header at the beginning of the stream.
    ///     Caller must ensure no concurrent mutations are happening that affect header fields.
    /// </summary>
    private void WriteHeader(Stream stream)
    {
        var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        stream.Position = 0;
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
        writer.Write(MaxLayersAllocated);
        writer.Write(VectorsInternal.Count);

        const int written = 4 + 2 + 4 * 8;
        const int padding = HeaderSize - written;
        
        for (var i = 0; i < padding; i++)
        {
            writer.Write((byte)0);
        }

        writer.Flush();
    }

    /// <summary>
    ///     Writes a single fixed-size slot to the stream at its current position.
    /// </summary>
    private void WriteSlot(Stream stream, in SlotSnapshot snapshot)
    {
        var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        if (!snapshot.IsAlive)
        {
            writer.Write((byte)0);
            
            // Pad the rest of the slot with zeros:
            var remaining = SlotSize - 1;
            for (var i = 0; i < remaining; i++)
            {
                writer.Write((byte)0);
            }

            writer.Flush();
            return;
        }

        writer.Write((byte)1); // Alive
        writer.Write(snapshot.TargetLayer);

        // Vector data:
        for (var j = 0; j < snapshot.VectorData.Length; j++)
        {
            writer.Write(snapshot.VectorData[j]);
        }

        // Dense edges:
        var denseCount = Math.Min(snapshot.DenseEdges.Length, MaxConnectionsDense);
        writer.Write(denseCount);
        for (var j = 0; j < MaxConnectionsDense; j++)
        {
            writer.Write(j < denseCount ? snapshot.DenseEdges[j] : 0);
        }

        // Sparse layers:
        for (var layer = 1; layer < MaxLayersAllocated; layer++)
        {
            var layerIdx = layer - 1;
            
            if (layerIdx < snapshot.SparseEdges.Length)
            {
                var edges = snapshot.SparseEdges[layerIdx];
                var sparseCount = Math.Min(edges.Length, MaxConnectionsLane);
                
                writer.Write(sparseCount);
            
                for (var j = 0; j < MaxConnectionsLane; j++)
                {
                    writer.Write(j < sparseCount ? edges[j] : 0);
                }
            }
            else
            {
                // This vector doesn't participate in the layer:
                writer.Write(0);
                
                for (var j = 0; j < MaxConnectionsLane; j++)
                {
                    writer.Write(0);
                }
            }
        }

        writer.Flush();
    }

    /// <summary>
    ///     Snapshots a vector's data into a local struct for writing without holding locks during IO.
    /// </summary>
    private SlotSnapshot SnapshotVector(StoredVectorImpl vector)
    {
        var vectorData = new float[Dimension];
        vector.VectorStorage.Block.Span.CopyTo(vectorData);

        var denseEdges = new int[vector.DenseGraph.Count];
        for (var i = 0; i < denseEdges.Length; i++)
        {
            denseEdges[i] = vector.DenseGraph[i];
        }

        var sparseEdges = new int[vector.TargetLayer][];
        for (var layer = 1; layer <= vector.TargetLayer; layer++)
        {
            var edges = vector.GetEdgesInLayer(layer);
            var arr = new int[edges.Count];
            for (var i = 0; i < arr.Length; i++)
            {
                arr[i] = edges[i];
            }

            sparseEdges[layer - 1] = arr;
        }

        return new SlotSnapshot(true, vector.TargetLayer, vectorData, denseEdges, sparseEdges);
    }
    
    private readonly struct SlotSnapshot(
        bool isAlive,
        int targetLayer,
        float[] vectorData,
        int[] denseEdges,
        int[][] sparseEdges)
    {
        public static readonly SlotSnapshot Dead = new(
            false,
            0, 
            [],
            [],
            []
        );

        public readonly bool IsAlive = isAlive;
        public readonly int TargetLayer = targetLayer;
        public readonly float[] VectorData = vectorData;
        public readonly int[] DenseEdges = denseEdges;
        public readonly int[][] SparseEdges = sparseEdges;
    }

    #endregion
    
    #region Load

    /// <summary>
    ///     Loads an index from a stream. Supports both format version 1 (legacy variable-length)
    ///     and version 2 (fixed-slot).
    /// </summary>
    public static MutableHnswIndex Load(Stream stream,
        int vectorPageSize = 512,
        int sparseEdgePageSize = 512 * 512,
        int denseEdgePageSize = 256 * 512,
        int baseLayerArrayPageCapacity = 512 * 512,
        int minLayerArrayPageSize = 32 * 512,
        int? seed = null)
    {
        if (stream is not BufferedStream)
        {
            stream = new BufferedStream(stream, BufferSize);
        }

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (reader.ReadByte() != 'H' || reader.ReadByte() != 'N' || reader.ReadByte() != 'S' || reader.ReadByte() != 'W')
        {
            throw new InvalidDataException("Invalid file header");
        }

        var version = reader.ReadUInt16();

        return version switch
        {
            LegacyFormatVersion => LoadV1(
                reader, 
                vectorPageSize, 
                sparseEdgePageSize,
                denseEdgePageSize,
                baseLayerArrayPageCapacity,
                minLayerArrayPageSize,
                seed
            ),
            CurrentFormatVersion => LoadV2(
                reader,
                stream,
                vectorPageSize,
                sparseEdgePageSize,
                denseEdgePageSize, 
                baseLayerArrayPageCapacity,
                minLayerArrayPageSize,
                seed
            ),
            _ => throw new InvalidDataException($"Unsupported format version {version}")
        };
    }
    
    private static MutableHnswIndex LoadV1(
        BinaryReader reader,
        int vectorPageSize,
        int sparseEdgePageSize,
        int denseEdgePageSize,
        int baseLayerArrayPageCapacity,
        int minLayerArrayPageSize,
        int? seed)
    {
        var dimension = reader.ReadInt32();
        var maxConnectionsLane = reader.ReadInt32();
        var maxConnectionsDense = reader.ReadInt32();
        var efConstruction = reader.ReadInt32();
        var layerCount = reader.ReadInt32();
        var entryPointIndex = reader.ReadInt32();
        var vectorCount = reader.ReadInt32();

        var index = new MutableHnswIndex(
            dimension, 
            maxConnectionsLane,
            maxConnectionsDense,
            efConstruction,
            seed: seed,
            vectorPageSize,
            sparseEdgePageSize,
            denseEdgePageSize,
            baseLayerArrayPageCapacity,
            minLayerArrayPageSize
        );

        // Free slots:
        var freeSlotCount = reader.ReadInt32();
        var freeSlots = new int[freeSlotCount];
        for (var i = 0; i < freeSlotCount; i++)
        {
            freeSlots[i] = reader.ReadInt32();
        }

        index.VectorsInternal.EnsureCapacity(vectorCount);
        
        for (var i = 0; i < vectorCount; i++)
        {
            index.VectorsInternal.Add(null);
        }

        // Read and allocate vectors:
        for (var i = 0; i < vectorCount; i++)
        {
            var isPresent = reader.ReadByte();

            if (isPresent == 0)
            {
                continue;
            }

            var targetLayer = reader.ReadInt32();

            var vectorData = new float[dimension];
            for (var j = 0; j < dimension; j++)
            {
                vectorData[j] = reader.ReadSingle();
            }

            var denseEdges = ReadEdgeList(reader);

            var sparseEdgesPerLayer = new int[targetLayer][];
            for (var layer = 1; layer <= targetLayer; layer++)
            {
                sparseEdgesPerLayer[layer - 1] = ReadEdgeList(reader);
            }

            var vector = index.AllocatePreDefinedVector(vectorData, targetLayer, i);

            for (var j = 0; j < denseEdges.Length; j++)
            {
                vector.DenseGraph.Add(denseEdges[j]);
            }

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

        index.LayerCount = layerCount;

        // Restore free slots in correct order:
        for (var i = freeSlotCount - 1; i >= 0; i--)
        {
            index._freeSlots.Push(freeSlots[i]);
        }

        // Compute MaxLayersAllocated from loaded data and assign FileOffset:
        var maxTargetLayer = 0;
        for (var i = 0; i < index.VectorsInternal.Count; i++)
        {
            var vector = index.VectorsInternal[i];
            
            if (vector != null && vector.TargetLayer > maxTargetLayer)
            {
                maxTargetLayer = vector.TargetLayer;
            }
        }

        index.MaxLayersAllocated = Math.Max(maxTargetLayer + 2, 1);
        for (var i = 0; i < index.VectorsInternal.Count; i++)
        {
            var vector = index.VectorsInternal[i];
            
            vector?.FileOffset = index.SlotOffset(i);
        }

        return index;
    }
    
    private static MutableHnswIndex LoadV2(
        BinaryReader reader, 
        Stream stream,
        int vectorPageSize,
        int sparseEdgePageSize,
        int denseEdgePageSize,
        int baseLayerArrayPageCapacity,
        int minLayerArrayPageSize,
        int? seed)
    {
        var dimension = reader.ReadInt32();
        var maxConnectionsLane = reader.ReadInt32();
        var maxConnectionsDense = reader.ReadInt32();
        var efConstruction = reader.ReadInt32();
        var layerCount = reader.ReadInt32();
        var entryPointIndex = reader.ReadInt32();
        var maxLayersAllocated = reader.ReadInt32();
        var vectorSlotCount = reader.ReadInt32();

        // ReSharper disable once UseObjectOrCollectionInitializer
        var index = new MutableHnswIndex(
            dimension, maxConnectionsLane, maxConnectionsDense, efConstruction,
            seed: seed,
            vectorPageSize, sparseEdgePageSize, denseEdgePageSize,
            baseLayerArrayPageCapacity, minLayerArrayPageSize
        );

        index.MaxLayersAllocated = maxLayersAllocated;

        // Skip any remaining header padding:
        stream.Position = HeaderSize;

        // Pre-size the vectors list with nulls:
        for (var i = 0; i < vectorSlotCount; i++)
        {
            index.VectorsInternal.Add(null);
        }
        
        // Read each slot:
        for (var i = 0; i < vectorSlotCount; i++)
        {
            var slotOffset = SlotOffset(i, index.SlotSize);
            stream.Position = slotOffset;

            var isAlive = reader.ReadByte();

            if (isAlive == 0)
            {
                // Add to free slots:
                index._freeSlots.Push(i);

                // Skip remaining bytes in this slot:
                stream.Position = slotOffset + index.SlotSize;
                continue;
            }

            var targetLayer = reader.ReadInt32();

            if (targetLayer < 0 || targetLayer > maxLayersAllocated)
            {
                throw new InvalidDataException($"Slot {i} has invalid targetLayer {targetLayer}");
            }

            var vectorData = new float[dimension];
            for (var j = 0; j < dimension; j++)
            {
                vectorData[j] = reader.ReadSingle();
            }

            // Dense edges:
            var denseEdgeCount = reader.ReadInt32();

            if (denseEdgeCount < 0)
            {
                throw new InvalidDataException($"Slot {i} has invalid denseEdgeCount {denseEdgeCount}");
            }

            // The slot stores MaxConnectionsDense values. Cap to that:
            var effectiveDenseCount = Math.Min(denseEdgeCount, maxConnectionsDense);
            var denseEdges = new int[effectiveDenseCount];
            for (var j = 0; j < effectiveDenseCount; j++)
            {
                denseEdges[j] = reader.ReadInt32();
            }

            // Always skip the remainder of the fixed-size dense section:
            var densePadding = maxConnectionsDense - effectiveDenseCount;
            if (densePadding > 0)
            {
                stream.Position += densePadding * 4;
            }

            // Sparse layers:
            var sparseEdgesPerLayer = new int[targetLayer][];
            for (var layer = 1; layer < maxLayersAllocated; layer++)
            {
                var sparseEdgeCount = reader.ReadInt32();
                var layerIdx = layer - 1;

                if (sparseEdgeCount < 0)
                {
                    throw new InvalidDataException($"Slot {i} layer {layer} has invalid sparseEdgeCount {sparseEdgeCount}");
                }

                // The slot stores MaxConnectionsLane values per layer:
                var effectiveSparseCount = Math.Min(sparseEdgeCount, maxConnectionsLane);

                if (layerIdx < targetLayer)
                {
                    sparseEdgesPerLayer[layerIdx] = new int[effectiveSparseCount];
                    for (var j = 0; j < effectiveSparseCount; j++)
                    {
                        sparseEdgesPerLayer[layerIdx][j] = reader.ReadInt32();
                    }
                }

                // Always skip the remainder of the fixed-size sparse section:
                var sparsePadding = maxConnectionsLane - effectiveSparseCount;
             
                if (sparsePadding > 0)
                {
                    stream.Position += sparsePadding * 4;
                }
            }

            // Allocate the vector:
            var vector = index.AllocatePreDefinedVector(vectorData, targetLayer, i);
            vector.FileOffset = slotOffset;

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

        index.LayerCount = layerCount;

        // Free slots were collected during the dead-slot scan above.
        // Reverse them so the stack pop order matches the original save order:
        var freeSlotsList = new List<int>(index._freeSlots);
        index._freeSlots.Clear();
        freeSlotsList.Sort();
        for (var i = freeSlotsList.Count - 1; i >= 0; i--)
        {
            index._freeSlots.Push(freeSlotsList[i]);
        }

        return index;
    }

    /// <summary>
    ///     Computes the file offset for the slot at <paramref name="slotIndex"/> using the given <paramref name="slotSize"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long SlotOffset(int slotIndex, int slotSize) => HeaderSize + (long)slotIndex * slotSize;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    #endregion
}
