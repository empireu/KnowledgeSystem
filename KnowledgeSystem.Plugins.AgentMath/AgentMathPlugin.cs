using KnowledgeSystem.PluginLoader;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Plugins.AgentMath;

[Plugin("AgentMath", "mqr.standard.agentmath", "0.0.0", "empireu")]
public class AgentMathPlugin(ILogger<AgentMathPlugin> logger) : IPlugin
{
    public Task Start(CancellationToken cancellationToken)
    {
        logger.LogInformation("AgentMath plugin started");

        return Task.CompletedTask;
    }
}
