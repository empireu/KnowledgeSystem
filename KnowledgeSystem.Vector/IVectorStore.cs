namespace KnowledgeSystem.Vector;

public interface IVectorStore : IReadOnlyVectorStore
{
    /// <summary>
    ///     Inserts a new vector into the database. Guaranteed to be thread-safe.
    /// </summary>
    public IStoredVector Insert(float[] data);
}