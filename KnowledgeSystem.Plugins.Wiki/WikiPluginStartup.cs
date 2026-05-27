using KnowledgeSystem.PluginLoader;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Wiki;

public class WikiPluginStartup : IPluginStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions<ApplicationOptions>()
            .BindConfiguration(ApplicationOptions.Section)
            .ValidateOnStart();

        // Stores:
        services.AddSingleton<WikiStores>();
        services.AddHostedService<WikiStores>(sp => sp.GetRequiredService<WikiStores>());
    }
}