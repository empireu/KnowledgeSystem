using KnowledgeSystem.PluginLoader;
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
    }
}