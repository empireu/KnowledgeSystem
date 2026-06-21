using KnowledgeSystem.PluginLoader;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Plugins.CodeMemory;

[Plugin("CodeMemory", "mqr.code.memory", "0.0.0", "empireu")]
public class CodeMemoryPlugin(ILogger<CodeMemoryPlugin> logger) : IPlugin
{
    public Task Start(CancellationToken cancellationToken)
    {
        logger.LogInformation("Started code memory");
        
        return Task.CompletedTask;
    }
}