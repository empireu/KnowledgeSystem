using KnowledgeSystem.PluginLoader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KnowledgeSystem.Plugins.RestApi;

public class RestApiPluginStartup : IPluginStartup
{
    public void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddOptions<RestApiOptions>()
            .BindConfiguration(RestApiOptions.Section)
            .ValidateOnStart();
    }
}
