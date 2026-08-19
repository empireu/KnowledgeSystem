using KnowledgeSystem.PluginLoader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KnowledgeSystem.Plugins.OreDb;

public class OreDbStartup : IPluginStartup
{
    public void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddOptions<OreDbOptions>()
            .BindConfiguration(OreDbOptions.Section)
            .ValidateOnStart();

        services.AddSingleton<OreDbStores>();
        services.AddHostedService<OreDbStores>(sp => sp.GetRequiredService<OreDbStores>());

        services.AddSingleton<OreDbImporter>();
        services.AddHostedService<OreDbImporter>(sp => sp.GetRequiredService<OreDbImporter>());
    }
}
