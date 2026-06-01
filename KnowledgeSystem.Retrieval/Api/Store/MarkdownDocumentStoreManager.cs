using KnowledgeSystem.Embedding;

namespace KnowledgeSystem.Retrieval.Api.Store;

/// <summary>
///     Manages the lifecycle of knowledge stores.
///     Supports dynamic creation, retrieval, and removal of stores at runtime.
/// </summary>
public sealed class MarkdownDocumentStoreManager(IServiceProvider serviceProvider, IEmbeddingService embeddingService)
{
    public readonly IServiceProvider ServiceProvider = serviceProvider;
    public readonly IEmbeddingService EmbeddingService = embeddingService;
    
    private readonly Dictionary<string, IReadOnlyMarkdownDocumentStore> _stores = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    #region API
    
    /// <summary>
    ///     Gets an active store by its ID, or null if not found.
    /// </summary>
    public IReadOnlyMarkdownDocumentStore? GetStore(string storeId)
    {
        lock (_lock)
        {
            return _stores.TryGetValue(storeId, out var store) ? store : null;
        }
    }

    /// <summary>
    ///     Gets a store by ID, throwing if not found.
    /// </summary>
    public IReadOnlyMarkdownDocumentStore GetRequiredStore(string storeId)
    {
        lock (_lock)
        {
            return GetStore(storeId) ?? throw new KeyNotFoundException($"Store with ID \"{storeId}\" not found");
        }
    }
    
    /// <summary>
    ///     Removes a store, releasing its resources.
    /// </summary>
    public async Task<bool> RemoveStoreAsync(string storeId, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            if (!_stores.Remove(storeId, out var store))
            {
                return false;
            }
            
            await store.DisposeAsync();

            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    ///     Gets the IDs of all active stores.
    /// </summary>
    public IReadOnlyList<string> GetActiveStoreIds()
    {
        lock (_lock)
        {
            return _stores.Keys.ToList();
        }
    }
    
    #endregion
    
    /// <summary>
    ///     Creates and registers a store by calling the <see cref="factory"/> in a locked region.
    ///     Not meant to be used directly!
    /// </summary>
    public async Task<TStore> CreateStoreAsync<TStore>(string storeId, Func<Task<TStore>> factory, CancellationToken cancellationToken = default) where TStore : IReadOnlyMarkdownDocumentStore
    {
        await  _semaphore.WaitAsync(cancellationToken);
    
        try
        {
            if (_stores.ContainsKey(storeId))
            {
                throw new InvalidOperationException($"Store with ID \"{storeId}\" already exists");
            }
            
            var result = await factory();
            
            _stores.Add(storeId, result);

            return result;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
