namespace KnowledgeSystem.Retrieval.Api;

// TODO increase these configs, they are incomplete

/// <summary>
///     Configuration for creating a disk-backed store.
/// </summary>
public sealed class DiskStoreConfiguration
{
    /// <summary>
    ///     Unique ID for the store.
    /// </summary>
    public required string StoreId { get; init; }

    /// <summary>
    ///     Path to the repository on disk.
    /// </summary>
    public required string RepositoryPath { get; init; }

    /// <summary>
    ///     Path to the SQLite database file.
    /// </summary>
    public required string DatabasePath { get; init; }

    /// <summary>
    ///     Path to the HNSW index file.
    /// </summary>
    public required string HnswIndexPath { get; init; }
}

/// <summary>
///     Configuration for creating an in-memory store.
/// </summary>
public sealed class InMemoryStoreConfiguration
{
    /// <summary>
    ///     Unique ID for the store.
    /// </summary>
    public required string StoreId { get; init; }
}
