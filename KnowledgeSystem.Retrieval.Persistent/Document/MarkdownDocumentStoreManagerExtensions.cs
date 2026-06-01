using KnowledgeSystem.Ai;
using KnowledgeSystem.Retrieval.Api.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeSystem.Retrieval.Persistent.Document;

public static class MarkdownDocumentStoreManagerExtensions
{
    /// <summary>
    ///     Creates a new disk-backed store and initializes it.
    ///     The store syncs its content from the repository on disk.
    /// </summary>
    public static async Task<IReadOnlyMarkdownDocumentStore> CreateStaticWikiStoreAsync(this MarkdownDocumentStoreManager manager, StaticDiskMarkdownWikiStoreConfig config, CancellationToken cancellationToken = default)
    {
        return await manager.CreateStoreAsync(config.StoreId, async () =>
        {
            var options = new WikiDiskMarkdownStoreDescription
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

            var dbContextOptions = new DbContextOptionsBuilder<DiskMarkdownDocumentStoreTrackerDbContext>()
                .UseSqlite($"Data Source={config.DatabasePath}")
                .Options;

            var dbContext = new DiskMarkdownDocumentStoreTrackerDbContext(dbContextOptions);
            var stateTracker = new SqliteMarkdownIndexStateTracker(dbContext);
            var logger = manager.ServiceProvider.GetRequiredService<ILogger<DiskMarkdownWikiStore>>();

            var store = new DiskMarkdownWikiStore(
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