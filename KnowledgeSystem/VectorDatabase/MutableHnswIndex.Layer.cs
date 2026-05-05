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
}