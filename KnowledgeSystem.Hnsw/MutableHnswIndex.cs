// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable LoopCanBeConvertedToQuery

using System.Runtime.CompilerServices;
using Microsoft.Extensions.ObjectPool;

namespace KnowledgeSystem.Hnsw;

public sealed partial class MutableHnswIndex
{
    public readonly int Dimension;
    public readonly int MaxConnectionsLane;
    public readonly int MaxConnectionsDense;
    public readonly int ExplorationFactorConstruction;
    
    private readonly double _recipLogMl;
    private readonly Random _random;
    
    internal readonly List<StoredVectorImpl?> VectorsInternal = [];
    private readonly Stack<int> _freeSlots = new();
    private readonly List<ScoredResult> _resultsBuffer = new(200);
    private readonly TrimEdgesData _trimEdgesData = new();
    private readonly List<int> _neighborSnapshotBuffer = [];

    private readonly ObjectPool<SearchData> _searchDataPool = new DefaultObjectPool<SearchData>(new SearchDataPoolPolicy(), 128);

    private sealed class SearchDataPoolPolicy : IPooledObjectPolicy<SearchData>
    {
        public SearchData Create()
        {
            return new SearchData();
        }

        public bool Return(SearchData obj)
        {
            obj.Clear();
            return true;
        }
    }
        
    /// <summary>
    ///     Storage for float arrays representing the vector data for each node.
    /// </summary>
    private readonly ArenaAllocator<float> _vectorAllocator;
    /// <summary>
    ///     Storage for integer arrays representing the connections of a node for sparse layers.
    /// </summary>
    private readonly ArenaAllocator<int> _laneEdgeStorageAllocator;
    /// <summary>
    ///     Storage for index arrays representing the connections of a node for the dense layer.
    /// </summary>
    private readonly ArenaAllocator<int> _denseEdgeStorageAllocator;
    /// <summary>
    ///     Allocator for the variable-length array of edge lists (each node has connections from 0 up to the target layer).
    /// </summary>
    private readonly BucketArenaAllocator<EdgeList> _layerStorageAllocator;
    
    /// <summary>
    ///     Gets the number of layers, including the dense layer.
    /// </summary>
    public int LayerCount { get; private set; } = 1;
    
    internal StoredVectorImpl? EntryPointVector;

    /// <summary>
    ///     Hierarchical navigable small world data structure (builder) for approximate K nearest vector lookup.
    ///     Lookups aren't very fast.
    /// </summary>
    /// <param name="dimension">The dimension of the stored vectors.</param>
    /// <param name="maxConnectionsLane">Maximum number of graph connections in higher layers.</param>
    /// <param name="maxConnectionsDense">Maximum number of graph connections in the dense layer.</param>
    /// <param name="efConstruction">The exploration factor for construction.</param>
    /// <param name="seed">The seed for the random number generator used during construction.</param>
    /// <param name="vectorPageSize">The number of vectors per allocation page.</param>
    /// <param name="sparseEdgePageSize">The number of edges per allocation page for the sparse graphs.</param>
    /// <param name="denseEdgePageSize">The number of edges per allocation page for the dense graph.</param>
    /// <param name="baseLayerArrayPageCapacity">The starting capacity for the layer array pages (see <see cref="MutableHnswIndex.BucketArenaAllocator{T}"/>).</param>
    /// <param name="minLayerArrayPageSize">The minimum capacity for the layer array pages (see <see cref="MutableHnswIndex.BucketArenaAllocator{T}"/>).</param>
    public MutableHnswIndex(
        int dimension,
        int maxConnectionsLane,
        int maxConnectionsDense, 
        int efConstruction = 200,
        int? seed = null,
        int vectorPageSize = 512,
        int sparseEdgePageSize = 512 * 512,
        int denseEdgePageSize = 256 * 512,
        int baseLayerArrayPageCapacity = 512 * 512,
        int minLayerArrayPageSize = 32 * 512)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConnectionsLane, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConnectionsDense, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(efConstruction, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(vectorPageSize, 2);
        
        Dimension = dimension;
        MaxConnectionsLane = maxConnectionsLane;
        MaxConnectionsDense = maxConnectionsDense;
        ExplorationFactorConstruction = efConstruction;

        _recipLogMl =  1.0 / Math.Log(MaxConnectionsLane);
        _random = seed.HasValue ? new Random(seed.Value) : new Random();

        _vectorAllocator = new ArenaAllocator<float>(dimension, vectorPageSize);
        _laneEdgeStorageAllocator = new ArenaAllocator<int>(maxConnectionsLane + 2, sparseEdgePageSize);
        _denseEdgeStorageAllocator = new ArenaAllocator<int>(maxConnectionsDense + 2, denseEdgePageSize);
        _layerStorageAllocator = new BucketArenaAllocator<EdgeList>(baseLayerArrayPageCapacity, minLayerArrayPageSize);
    }
    
    /// <summary>
    ///     Gets the vectors indexed by <see cref="IStoredVector.Index"/>.
    /// </summary>
    public IReadOnlyList<IStoredVector?> Vectors => VectorsInternal;

    /// <summary>
    ///     Fixed-size list for node indices.
    /// </summary>
    /// <param name="edgeCapacity">The fixed maximum capacity of the list.</param>
    /// <param name="storage">The backing storage. Must be <see cref="EdgeCapacity"/> + 1 in length.</param>
    internal readonly struct EdgeList(int edgeCapacity, Allocation<int> storage)
    {
        public readonly int EdgeCapacity = edgeCapacity;
        public readonly Allocation<int> Storage = storage;

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Storage.Block.Span[0];
        }

        public int this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Storage.Block.Span[1 + index];
        }

        /// <summary>
        ///     Adds an element to the edge list.
        /// </summary>
        /// <param name="edge"></param>
        /// <exception cref="InvalidOperationException">Thrown if the list is full.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int edge)
        {
            var count = Count;

            if (count == EdgeCapacity)
            {
                throw new InvalidOperationException("Edge list is full");
            }

            var span = Storage.Block.Span;
            span[1 + count] = edge;
            span[0] = count + 1;
        }

        /// <summary>
        ///     Removes the first occurrence of <paramref name="edge"/> by swapping the last element into its position.
        /// </summary>
        /// <returns>True if the element was found and removed. Otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(int edge)
        {
            var span = Storage.Block.Span;
            var count = span[0];

            for (var i = 0; i < count; i++)
            {
                if (span[1 + i] != edge)
                {
                    continue;
                }

                span[1 + i] = span[count];
                span[0] = count - 1;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Zeroes out the allocated block.
        /// </summary>
        public void Clear()
        {
            Storage.Block.Span.Clear();
        }
    }
    
    internal sealed class StoredVectorImpl(int index, Allocation<float> vectorStorage, EdgeList denseGraph, Allocation<EdgeList>? sparseGraphs) : IStoredVector
    {
        public int Index { get; } = index;
        
        /// <summary>
        ///     The vector data.
        /// </summary>
        public readonly Allocation<float> VectorStorage = vectorStorage;
        
        /// <summary>
        ///     The dense graph data. Always present.
        /// </summary>
        public readonly EdgeList DenseGraph = denseGraph;
        
        /// <summary>
        ///     The sparse graph data, allocated up to the target layer.
        ///     Only set if this vector isn't exclusively networked in the dense graph.
        /// </summary>
        public readonly Allocation<EdgeList>? SparseGraphs = sparseGraphs;

        /// <summary>
        ///     The target layer. If non-zero, then <see cref="SparseGraphs"/> will be populated.
        /// </summary>
        public int TargetLayer => SparseGraphs.HasValue 
            ? SparseGraphs.Value.Block.Length 
            : 0;
        
        public ReadOnlySpan<float> VectorView =>  VectorStorage.Block.Span;
        
        public void Load(ReadOnlySpan<float> data)
        {
            var storage = VectorStorage.Block.Span;
            
            if (data.Length != storage.Length)
            {
                throw new ArgumentException($"Cannot load data vector of dimension {data.Length} into vector of dimension {storage.Length}");
            }
            
            data.CopyTo(storage);
        }

        /// <summary>
        ///     Gets the connections in layer <see cref="Index"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EdgeList GetEdgesInLayer(int index)
        {
            if (index == 0)
            {
                return DenseGraph;
            }

            // ReSharper disable once ConvertIfStatementToReturnStatement
            if (!SparseGraphs.HasValue)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Tried to get sparse layer from a node that doesn't have any");
            }

            return SparseGraphs.Value.Block.Span[index - 1];
        }
    }
}