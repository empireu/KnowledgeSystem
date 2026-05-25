namespace KnowledgeSystem.Vector;

public interface IVectorStore : IReadOnlyVectorStore
{
    /// <summary>
    ///     Inserts a new vector into the database. Guaranteed to be thread-safe.
    /// </summary>
    public IStoredVector Insert(float[] data);
    
    /// <summary>
    ///     Removes the vector from the store. Guaranteed to be thread-safe.
    /// </summary>
    /// <returns>True if the vector was found and removed. Otherwise, false.</returns>
    public bool Remove(IStoredVector vector);
}