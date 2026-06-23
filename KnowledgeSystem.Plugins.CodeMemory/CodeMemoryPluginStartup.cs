using KnowledgeSystem.PluginLoader;
using KnowledgeSystem.Plugins.CodeMemory.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KnowledgeSystem.Plugins.CodeMemory;

public class CodeMemoryPluginStartup : IPluginStartup
{
    public void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddOptions<MemorySystemConfig>()
            .BindConfiguration(MemorySystemConfig.Section)
            .ValidateOnStart();

        services.AddSingleton<MemoryExtractionService>();
        services.AddHostedService<MemoryExtractionService>(sp => sp.GetRequiredService<MemoryExtractionService>());
        services.AddSingleton<MemoryStoreService>();
        services.AddHostedService<MemoryStoreService>(sp => sp.GetRequiredService<MemoryStoreService>());
        services.AddSingleton<RecallSystem>();
    }
}