using KnowledgeSystem.PluginLoader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KnowledgeSystem.Plugins.AgentMath;

public class AgentMathPluginStartup : IPluginStartup
{
    public void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddOptions<AgentMathOptions>()
            .BindConfiguration(AgentMathOptions.Section);

        services.AddSingleton<AgentMathEvaluator>();
    }
}
