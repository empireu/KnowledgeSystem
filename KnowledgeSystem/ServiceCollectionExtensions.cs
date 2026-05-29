using KnowledgeSystem.Agents.Telemetry;
using KnowledgeSystem.Api;
using KnowledgeSystem.Discord.Conversation;
using KnowledgeSystem.Embedding;
using KnowledgeSystem.Reranking;
using KnowledgeSystem.Retrieval.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace KnowledgeSystem;

/// <summary>
///     Extension methods for registering RAG services with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IHostBuilder hostBuilder)
    {
        internal IHostBuilder WithDiscordIntegration() => hostBuilder.ConfigureServices(services =>
        {
            services.AddDiscordGateway(options =>
            {
                options.Intents = GatewayIntents.GuildMessages | GatewayIntents.MessageContent | GatewayIntents.Guilds;
            });

            // Message handler:
            services.AddGatewayHandler<ExternalMessageHandler>();
        });

        internal IHostBuilder WithCoreServices() => hostBuilder.ConfigureServices((context, services) =>
        {
            var options = context.Configuration
                .GetSection(KnowledgeSystemConfig.Section)
                .Get<KnowledgeSystemConfig>() ?? throw new InvalidOperationException("RAG configuration is missing");
            
            // Conversation manager:
            services.AddSingleton<ConversationManager>();
            services.AddSingleton<IConversationManager>(sp => sp.GetRequiredService<ConversationManager>());
            services.AddHostedService(sp => sp.GetRequiredService<ConversationManager>());

            // Run tracker:
            services.AddSingleton<ResponseTracker>();
            services.AddSingleton<IResponseTracker>(sp => sp.GetRequiredService<ResponseTracker>());
            services.AddHostedService<ResponseTracker>(sp => sp.GetRequiredService<ResponseTracker>());
            
            // Embedding:
            if (options is { EmbeddingProvider: not null, EmbeddingConfig: not null })
            {
                services.AddSingleton<IEmbeddingService>(_ =>
                    new OpenAiEmbeddingService(
                        options.EmbeddingProvider.Endpoint,
                        options.EmbeddingProvider.Key,
                        options.EmbeddingProvider.Model,
                        options.EmbeddingConfig.Dimension,
                        options.EmbeddingConfig.SystemPrompt
                    ));
            }
            
            // Reranking:
            if (options.RerankingProvider != null)
            {
                services.AddSingleton<IRerankingService>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<RawRestRerankingService>>();

                    return new RawRestRerankingService(
                        logger,
                        options.RerankingProvider.Endpoint,
                        options.RerankingProvider.Model,
                        options.RerankingProvider.Key
                    );
                });
            }
        });

        internal IHostBuilder WithTelemetryServices() => hostBuilder.ConfigureServices(services =>
        {
            services
                .AddOpenTelemetry()
                .ConfigureResource(resource => 
                {
                    resource.AddService("KnowledgeSystem");
                })
                .WithTracing(tracing =>
                {
                    tracing.AddSource(RetrievalTelemetry.Retrieval.Name);
                    tracing.AddSource(KnowledgeSystemTelemetry.AgentTools.Name);
                    tracing.AddSource(AgentTelemetry.Agent.Name);
                    tracing.AddSource(KnowledgeSystemTelemetry.AgentChat.Name);
                
                    tracing.AddOtlpExporter(options => 
                    {
                        options.Endpoint = new Uri("http://localhost:4317");
                        options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    });
                });
        });
    }
}
