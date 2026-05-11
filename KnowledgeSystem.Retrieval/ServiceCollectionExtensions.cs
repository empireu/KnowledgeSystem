using KnowledgeSystem.Retrieval.Data;
using KnowledgeSystem.Retrieval.Embeddings;
using KnowledgeSystem.Retrieval.Engine;
using KnowledgeSystem.Retrieval.Reranking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Retrieval;

/// <summary>
///     Extension methods for registering RAG services with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRagServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RagOptions>()
            .BindConfiguration(RagOptions.Section)
            .ValidateOnStart();
 
        var options = configuration.GetSection(RagOptions.Section).Get<RagOptions>() 
                      ?? throw new InvalidOperationException("RAG configuration is missing");

        services.AddDbContext<RagDbContext>(o => o.UseSqlite($"Data Source={options.DatabasePath}"));

        services.AddSingleton<IEmbeddingService>(_ =>
            new OpenAiEmbeddingService(
                options.EmbeddingEndpoint,
                options.EmbeddingModel,
                options.EmbeddingApiKey,
                options.EmbeddingDimension
            ));

        services.AddSingleton<IRerankingService>(sp => 
            new RawRestRerankingService(
                sp.GetRequiredService<ILogger<RawRestRerankingService>>(),
                options.RerankinggEndpoint,
                options.RerankingModel,
                options.RerankingApiKey
            ));
        
        services.AddSingleton<RagEngine>();
        
        return services;
    }
}
