namespace KnowledgeSystem.Retrieval.Api;

/// <summary>
///     Manages the lifecycle of knowledge stores.
///     Supports dynamic creation, retrieval, and removal of stores at runtime.
/// </summary>
public interface IStoreManager
{
    /// <summary>
    ///     Gets an active store by its ID, or null if not found.
    /// </summary>
    IReadOnlyDocumentStore? GetStore(string storeId);

    /// <summary>
    ///     Gets a store by ID, throwing if not found.
    /// </summary>
    IReadOnlyDocumentStore GetRequiredStore(string storeId);

    /// <summary>
    ///     Creates a new disk-backed store and initializes it.
    ///     The store syncs its content from the repository on disk.
    /// </summary>
    Task<IReadOnlyDocumentStore> CreateDiskStoreAsync(DiskStoreConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates a new in-memory store.
    ///     The store starts empty and supports adding documents at runtime.
    /// </summary>
    Task<IReadOnlyDocumentStore> CreateInMemoryStoreAsync(InMemoryStoreConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Removes a store, releasing its resources.
    /// </summary>
    Task RemoveStoreAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the IDs of all active stores.
    /// </summary>
    IReadOnlyList<string> GetActiveStoreIds();
}
