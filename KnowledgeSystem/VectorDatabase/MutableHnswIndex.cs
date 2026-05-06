// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable LoopCanBeConvertedToQuery

namespace KnowledgeSystem.VectorDatabase;

public sealed partial class MutableHnswIndex
{
    public readonly int Dimension;
    public readonly int MaxConnectionsLane;
    public readonly int MaxConnectionsDense;
    public readonly int ExplorationFactorConstruction;
    
    internal readonly List<ILayer> Layers;
    private readonly double _recipLogMl;
    private readonly Random _random;
    
    private readonly List<StoredVectorImpl> _vectors = [];
    private readonly SearchData _searchData = new();
    private readonly List<ScoredResult> _resultsBuffer = new(200);
    private readonly TrimEdgesData _trimEdgesData = new();
    private readonly ArenaAllocator<float> _vectorAllocator;
    
    private StoredVectorImpl? _entryPointVector;

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
    public MutableHnswIndex(
        int dimension,
        int maxConnectionsLane,
        int maxConnectionsDense, 
        int efConstruction = 200,
        int? seed = null,
        int vectorPageSize = 512)
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
        Layers = [new DenseLayer(0, MaxConnectionsDense)];

        _recipLogMl =  1.0 / Math.Log(MaxConnectionsLane);
        _random = seed.HasValue ? new Random(seed.Value) : new Random();

        _vectorAllocator = new ArenaAllocator<float>(dimension, vectorPageSize);
    }
    
    public IReadOnlyList<IStoredVector> Vectors => _vectors;

    private sealed class StoredVectorImpl(int index, Allocation<float> storage) : IStoredVector
    {
        public int Index { get; } = index;
        
        public readonly Allocation<float> Storage = storage;

        public ReadOnlySpan<float> StorageView =>  Storage.Block.Span;

        public void Load(ReadOnlySpan<float> data)
        {
            var storage = Storage.Block.Span;
            
            if (data.Length != storage.Length)
            {
                throw new ArgumentException($"Cannot load data vector of dimension {data.Length} into vector of dimension {storage.Length}");
            }
            
            data.CopyTo(storage);
        }
    }
}