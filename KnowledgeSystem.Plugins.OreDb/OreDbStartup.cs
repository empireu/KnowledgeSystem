using KnowledgeSystem.PluginLoader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KnowledgeSystem.Plugins.OreDb;

public class OreDbStartup : IPluginStartup
{
    public void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        
    }
}