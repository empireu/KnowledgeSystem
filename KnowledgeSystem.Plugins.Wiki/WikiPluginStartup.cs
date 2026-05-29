using KnowledgeSystem.PluginLoader;
using KnowledgeSystem.Plugins.Wiki.Agents.Wiki;
using KnowledgeSystem.Plugins.Wiki.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KnowledgeSystem.Plugins.Wiki;

public class WikiPluginStartup : IPluginStartup
{
    public void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddOptions<WikiOptions>()
            .BindConfiguration(WikiOptions.Section)
            .ValidateOnStart();

        // Stores:
        services.AddSingleton<WikiStores>();
        services.AddHostedService<WikiStores>(sp => sp.GetRequiredService<WikiStores>());

        services.AddSingleton<WikiLayerFactory>();
        
        var config = context.Configuration
            .GetRequiredSection(WikiOptions.Section)
            .Get<WikiOptions>();

        if (config?.Memory != null)
        {
            // Memory system:
            services.AddSingleton<MemoryExtractionService>();
            services.AddHostedService<MemoryExtractionService>(sp => sp.GetRequiredService<MemoryExtractionService>());
            services.AddSingleton<IMemoryExtractionService>(sp => sp.GetRequiredService<MemoryExtractionService>());
            services.AddSingleton<MemoryStoreService>();
            services.AddHostedService<MemoryStoreService>(sp => sp.GetRequiredService<MemoryStoreService>());
        }
    }
}