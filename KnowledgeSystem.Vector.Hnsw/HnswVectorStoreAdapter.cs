namespace KnowledgeSystem.Vector.Hnsw;

public sealed class HnswVectorStoreAdapter(MutableHnswIndex index, int efSearch = 1000) : IReadOnlyVectorStore
{
    /// <summary>
    ///     The underlying HNSW index.
    /// </summary>
    public readonly MutableHnswIndex Index = index;

    public int Dimension => Index.Dimension;

    /// <summary>
    ///     The constant efSearch.
    /// </summary>
    public readonly int EfSearch = efSearch;

    public IStoredVector GetVector(int index1) => Index.Vectors[index1]!;

    public VectorSearchResult[] Search(ReadOnlySpan<float> query, int k) => Index.Search(query, k, EfSearch);
}
