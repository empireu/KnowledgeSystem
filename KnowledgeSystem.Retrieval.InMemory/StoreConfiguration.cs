namespace KnowledgeSystem.Retrieval.InMemory;

// TODO increase these configs, they are incomplete

/// <summary>
///     Configuration for creating an in-memory store.
/// </summary>
public sealed class InMemoryStoreDescription
{
    /// <summary>
    ///     Unique ID for the store.
    /// </summary>
    public required string StoreId { get; init; }
}
