using System.Runtime.CompilerServices;

namespace KnowledgeSystem.VectorDatabase;

public sealed partial class MutableHnswIndex
{
    /// <summary>
    ///     Graph on the vectors in the data structure.
    /// </summary>
    internal interface ILayer
    {
        int Index { get; }

        /// <summary>
        ///     Gets the edge list for the given node or null, if the storage for that node isn't created yet.
        /// </summary>
        bool TryGetEdges(int nodeIndex, out List<int>? edges);

        /// <summary>
        ///     Gets the edge list for the given node, creating it if necessary.
        /// </summary>
        List<int> GetOrCreateEdges(int nodeIndex);
    }

    /// <summary>
    ///     Graph for layer 0, which can have edges for all nodes.
    ///     It doesn't make sense to use a dictionary as the backing storage, so we just use a list indexed by the vector's index.
    /// </summary>
    internal sealed class DenseLayer(int index, int perNodeCapacity) : ILayer
    {
        public int Index { get; } = index;

        internal readonly List<List<int>?> _edges = new();

        public bool TryGetEdges(int nodeIndex, out List<int>? edges)
        {
            if (nodeIndex < _edges.Count)
            {
                edges = _edges[nodeIndex];
                return edges != null;
            }

            edges = null;
            return false;
        }

        public List<int> GetOrCreateEdges(int nodeIndex)
        {
            while (_edges.Count <= nodeIndex)
            {
                _edges.Add(null);
            }

            var edges = _edges[nodeIndex];
            if (edges == null)
            {
                edges = new List<int>(perNodeCapacity);
                _edges[nodeIndex] = edges;
            }

            return edges;
        }
    }

    /// <summary>
    ///     Graph for higher layers.
    ///     Uses a sparse backing collection.
    /// </summary>
    private sealed class SparseLayer(int index, int perNodeCapacity) : ILayer
    {
        public int Index { get; } = index;

        private readonly Dictionary<int, List<int>> _edges = new();

        public bool TryGetEdges(int nodeIndex, out List<int>? edges)
        {
            return _edges.TryGetValue(nodeIndex, out edges);
        }

        public List<int> GetOrCreateEdges(int nodeIndex)
        {
            if (!_edges.TryGetValue(nodeIndex, out var edges))
            {
                edges = new List<int>(perNodeCapacity);
                _edges.Add(nodeIndex, edges);
            }

            return edges;
        }
    }

    /// <summary>
    ///     Fixed-size list for node indices.
    /// </summary>
    /// <param name="edgeCapacity">The fixed maximum capacity of the list.</param>
    /// <param name="storage">The backing storage. Must be <see cref="EdgeCapacity"/> + 1 in length.</param>
    internal readonly struct EdgeList(int edgeCapacity, Memory<int> storage)
    {
        public readonly int EdgeCapacity = edgeCapacity;
        public readonly Memory<int> Storage = storage;

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Storage.Span[0];
        }

        public int this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Storage.Span[1 + index];
        }

        public void Add(int edge)
        {
            var count = Count;

            if (count == EdgeCapacity)
            {
                throw new InvalidOperationException("Edge list is full");
            }

            var span = Storage.Span;
            span[1 + count] = edge;
            span[0] = count + 1;
        }

        /// <summary>
        ///     Removes the first occurrence of <paramref name="edge"/> by swapping the last element into its position.
        /// </summary>
        /// <returns>True if the element was found and removed. Otherwise, false.</returns>
        public bool Remove(int edge)
        {
            var span = Storage.Span;
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
    }
}