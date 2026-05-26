using KnowledgeSystem.Embedding;
using KnowledgeSystem.Retrieval.Api;
using KnowledgeSystem.Retrieval.Api.Store;
using KnowledgeSystem.Retrieval.Persistent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem;

/// <summary>
///     Extension methods for registering RAG services with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRagServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WikiDiskStoreDescription>()
            .BindConfiguration(WikiDiskStoreDescription.Section)
            .ValidateOnStart();
 
        var options = configuration.GetSection(WikiDiskStoreDescription.Section).Get<WikiDiskStoreDescription>() 
                      ?? throw new InvalidOperationException("RAG configuration is missing");

        services.AddDbContext<RagDbContext>(o => o.UseSqlite($"Data Source={options.DatabasePath}"));

        // Register the SQLite-backed index state tracker:
        services.AddSingleton<IIndexStateTracker, SqliteIndexStateTracker>();

        services.AddSingleton<IEmbeddingService>(_ =>
            new OpenAiEmbeddingService(
                options.Embedding.Endpoint,
                options.Embedding.Key,
                options.Embedding.Model,
                options.Embedding.Dimension,
                options.Embedding.SystemPrompt
            ));
        
        services.AddSingleton<DiskWikiStore>();
        
        // TODO Move to manager
        services.AddSingleton<IReadOnlyDocumentStore>(sp => sp.GetRequiredService<DiskWikiStore>());
        services.AddSingleton<StoreManager>();
        
        return services;
    }
}
