using KnowledgeSystem.PluginLoader;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Hosting.Services.ApplicationCommands;

namespace KnowledgeSystem.Plugins.OreDb;

[Plugin("OreDb", "mqr.game.oredb", "0.0.0", "empireu")]
public sealed class OreDbPlugin(ILogger<OreDbPlugin> logger, IHost host) : IPlugin
{
    public Task Start(CancellationToken cancellationToken)
    {
        host.AddApplicationCommandModule<OreDbModule>();
        
        logger.LogInformation("Registered OreDb commands");

        return Task.CompletedTask;
    }
}