using KnowledgeSystem.PluginLoader;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Hosting.Services.ApplicationCommands;

namespace KnowledgeSystem.Plugins.Wiki;

[Plugin("Wiki", "mqr.standard.wiki", "0.0.0", "empireu")]
public class WikiPlugin(ILogger<WikiPlugin> logger, IHost host) : IPlugin
{
    public Task Start(CancellationToken cancellationToken)
    {
        host.AddApplicationCommandModule<WikiModule>();
        
        logger.LogInformation("Wiki plugin loaded");
        
        return Task.CompletedTask;
    }
}