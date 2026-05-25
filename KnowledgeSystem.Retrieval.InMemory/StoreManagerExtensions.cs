using KnowledgeSystem.Retrieval.Api.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Retrieval.InMemory;

public static class StoreManagerExtensions
{
    /// <summary>
    ///     Creates a new in-memory store.
    ///     The store starts empty and supports adding documents at runtime.
    /// </summary>
    public static async Task<IReadOnlyDocumentStore> CreateInMemoryStoreAsync(this StoreManager manager, InMemoryStoreDescription description, CancellationToken cancellationToken = default)
    {
        return await manager.CreateStoreAsync(description.StoreId, () =>
        {
            // In-memory stores use the InMemoryIndexStateTracker and don't load from disk
            // They require an IDocumentStore implementation for adding documents at runtime
            // For now, create a lightweight in-memory store backed by the InMemoryIndexStateTracker
            var stateTracker = new InMemoryIndexStateTracker();
            var logger = manager.ServiceProvider.GetRequiredService<ILogger<InMemoryDocumentStore>>();

            var store = new InMemoryDocumentStore(
                logger,
                description.StoreId, 
                stateTracker,
                manager.EmbeddingService
            );
            
            return Task.FromResult<IReadOnlyDocumentStore>(store);
        }, cancellationToken);
    }
}