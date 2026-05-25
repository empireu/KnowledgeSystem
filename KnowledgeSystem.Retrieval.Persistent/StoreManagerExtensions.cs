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
    public static async Task<IReadOnlyDocumentStore> CreateDiskStoreAsync(this StoreManager manager, DiskStoreConfig config, CancellationToken cancellationToken = default)
    {
        return await manager.CreateStoreAsync(config.StoreId, async () =>
        {
            // TODO This crap needs to be refactord
            var options = new RagOptions
            {
                StoreId = config.StoreId,
                RepositoryPath = config.RepositoryPath,
                DatabasePath = config.DatabasePath,
                HnswIndexPath = config.HnswIndexPath,
                EmbeddingEndpoint = "",
                EmbeddingModel = "",
                EmbeddingApiKey = "",
                EmbeddingDimension = manager.EmbeddingService.Dimension
            };

            var dbContextOptions = new DbContextOptionsBuilder<RagDbContext>()
                .UseSqlite($"Data Source={config.DatabasePath}")
                .Options;

            var dbContext = new RagDbContext(dbContextOptions);
            var stateTracker = new SqliteIndexStateTracker(dbContext);
            var logger = manager.ServiceProvider.GetRequiredService<ILogger<RagEngine>>();

            var engine = new RagEngine(
                logger,
                stateTracker,
                manager.EmbeddingService,
                Options.Create(options)
            );
            
            await engine.InitializeAsync(cancellationToken);

            return engine;
        }, cancellationToken);
    }
}