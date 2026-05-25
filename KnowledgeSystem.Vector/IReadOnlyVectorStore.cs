namespace KnowledgeSystem.Vector;

public interface IReadOnlyVectorStore
{
    /// <summary>
    ///     Gets the dimension of the vector space.
    /// </summary>
    public int Dimension { get; }

    /// <summary>
    ///     Gets the vector by index.
    /// </summary>
    public IStoredVector GetVector(int index);
    
    /// <summary>
    ///     Searches for the approximate <see cref="k"/> vectors most similar to <see cref="query"/>.
    /// </summary>
    /// <param name="query">A vector matching the <see cref="Dimension"/>.</param>
    /// <param name="k">The maximum number of vectors to explore.</param>
    public VectorSearchResult[] Search(ReadOnlySpan<float> query, int k);
}