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
    private readonly MarkdownDocumentStoreManager _storeManager = ActivatorUtilities.CreateInstance<MarkdownDocumentStoreManager>(serviceProvider);

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

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Empty
        
        return Task.CompletedTask;
    }
}