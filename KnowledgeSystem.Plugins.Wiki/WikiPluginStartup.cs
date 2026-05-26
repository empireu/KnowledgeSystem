using KnowledgeSystem.Agent.Config;
using KnowledgeSystem.PluginLoader;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Agent;

public class WikiPluginStartup : IPluginStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions<ApplicationOptions>()
            .BindConfiguration(ApplicationOptions.Section)
            .ValidateOnStart();
    }
}