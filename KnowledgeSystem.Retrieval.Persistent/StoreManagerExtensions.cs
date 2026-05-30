using KnowledgeSystem.Ai;
using KnowledgeSystem.Retrieval.Api.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeSystem.Retrieval.Persistent;

public static class StoreManagerExtensions
{
    /// <summary>
    ///     Creates a new disk-backed store and initializes it.
    ///     The store syncs its content from the repository on disk.
    /// </summary>
    public static async Task<IReadOnlyMarkdownDocumentStore> CreateStaticWikiStoreAsync(this StoreManager manager, StaticDiskWikiStoreConfig config, CancellationToken cancellationToken = default)
    {
        return await manager.CreateStoreAsync(config.StoreId, async () =>
        {
            var options = new WikiDiskStoreDescription
            {
                StoreId = config.StoreId,
                RepositoryPath = config.RepositoryPath,
                DatabasePath = config.DatabasePath,
                HnswIndexPath = config.HnswIndexPath,
                Embedding = new EmbeddingConfig
                {
                    Dimension = manager.EmbeddingService.Dimension
                },
                MaxChunkLength = 1000
            };

            var dbContextOptions = new DbContextOptionsBuilder<RagDbContext>()
                .UseSqlite($"Data Source={config.DatabasePath}")
                .Options;

            var dbContext = new RagDbContext(dbContextOptions);
            var stateTracker = new SqliteIndexStateTracker(dbContext);
            var logger = manager.ServiceProvider.GetRequiredService<ILogger<DiskWikiStore>>();

            var store = new DiskWikiStore(
                logger,
                stateTracker,
                manager.EmbeddingService,
                Options.Create(options)
            );
            
            await store.InitializeAsync(cancellationToken);

            return store;
        }, cancellationToken);
    }
}