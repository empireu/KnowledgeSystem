using KnowledgeSystem.Retrieval.Api.Store;
using KnowledgeSystem.Retrieval.Persistent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeSystem.Plugins.Wiki;

public class WikiStores(ILogger<WikiStores> logger, IServiceProvider serviceProvider, IOptions<WikiOptions> options) : IHostedService
{
    private readonly StoreManager _storeManager = ActivatorUtilities.CreateInstance<StoreManager>(serviceProvider);

    public IReadOnlyDocumentStore Store { get; private set; } = null!;
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = options.Value;
        
        logger.LogInformation("Loading wiki from {path}", config.RepoPath);
        
        // Might corrupt the HNSW on cancellation:
        // ReSharper disable once MethodSupportsCancellation
        Store = await _storeManager.CreateStaticWikiStoreAsync(new StaticDiskWikiStoreConfig
        {
            StoreId = "wiki",
            RepositoryPath = config.RepoPath,
            DatabasePath = $"{config.RepoPath}_tracker.db",
            HnswIndexPath = $"{config.RepoPath}_hnsw.bin"
        });
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Empty
        
        return Task.CompletedTask;
    }
}