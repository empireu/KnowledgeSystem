using KnowledgeSystem.Retrieval.Api.Store;
using KnowledgeSystem.Retrieval.Persistent;
using KnowledgeSystem.Retrieval.Persistent.Document;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeSystem.Plugins.Wiki;

public class WikiStores(ILogger<WikiStores> logger, IServiceProvider serviceProvider, IOptions<WikiOptions> options) : IHostedService
{
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private MarkdownDocumentStoreManager _storeManager = ActivatorUtilities.CreateInstance<MarkdownDocumentStoreManager>(serviceProvider);

    public IReadOnlyMarkdownDocumentStore Store { get; private set; } = null!;
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = options.Value;
        
        logger.LogInformation("Loading wiki from {path}", config.RepoPath);
        
        // Might corrupt the HNSW on cancellation:
        // ReSharper disable once MethodSupportsCancellation
        Store = await _storeManager.CreateStaticWikiStoreAsync(new StaticDiskMarkdownWikiStoreConfig
        {
            StoreId = "wiki",
            RepositoryPath = config.RepoPath,
            DatabasePath = $"{config.RepoPath}_tracker.db",
            HnswIndexPath = $"{config.RepoPath}_hnsw.bin"
        });
    }
    
    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        
        try
        {
            var config = options.Value;
            
            logger.LogInformation("Reloading wiki from {path}", config.RepoPath);
            
            var freshManager = ActivatorUtilities.CreateInstance<MarkdownDocumentStoreManager>(serviceProvider);
            
            // Might corrupt the HNSW on cancellation:
            // ReSharper disable once MethodSupportsCancellation
            var freshStore = await freshManager.CreateStaticWikiStoreAsync(new StaticDiskMarkdownWikiStoreConfig
            {
                StoreId = "wiki",
                RepositoryPath = config.RepoPath,
                DatabasePath = $"{config.RepoPath}_tracker.db",
                HnswIndexPath = $"{config.RepoPath}_hnsw.bin"
            });
            
            _storeManager = freshManager;
            Store = freshStore;
            
            logger.LogInformation("Wiki reload complete");
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Empty
        
        return Task.CompletedTask;
    }
}