namespace KnowledgeSystem.Vector;

public interface IReadOnlyVectorStore
{
    /// <summary>
    ///     Gets the dimension of the vector space.
    /// </summary>
    public int Dimension { get; }
    
    /// <summary>
    ///     Searches for the approximate <see cref="k"/> vectrs most similar to <see cref="query"/>.
    /// </summary>
    /// <param name="query">A vector matching the <see cref="Dimension"/>.</param>
    /// <param name="k">The maximum number of vectors to explore.</param>
    public VectorSearchResult[] Search(ReadOnlySpan<float> query, int k);
}