using KnowledgeSystem.PluginLoader;
using KnowledgeSystem.Plugins.Wiki.Config;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Wiki;

public class WikiPluginStartup : IPluginStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions<ApplicationOptions>()
            .BindConfiguration(ApplicationOptions.Section)
            .ValidateOnStart();
    }
}