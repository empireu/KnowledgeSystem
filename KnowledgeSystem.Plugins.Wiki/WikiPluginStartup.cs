using KnowledgeSystem.PluginLoader;
using KnowledgeSystem.Plugins.Wiki.Agents.Wiki;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Wiki;

public class WikiPluginStartup : IPluginStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions<WikiOptions>()
            .BindConfiguration(WikiOptions.Section)
            .ValidateOnStart();

        // Stores:
        services.AddSingleton<WikiStores>();
        services.AddHostedService<WikiStores>(sp => sp.GetRequiredService<WikiStores>());

        services.AddSingleton<WikiLayerFactory>();
    }
}