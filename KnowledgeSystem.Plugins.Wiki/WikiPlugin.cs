using KnowledgeSystem.PluginLoader;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Services.ApplicationCommands;

namespace KnowledgeSystem.Plugins.Wiki;

[Plugin("Wiki", "mqr.standard.wiki", "0.0.0", "empireu")]
[PluginDependency("mqr.standard.agentmath", PluginDependencyAttribute.DependencyType.Optional)]
public class WikiPlugin(
    ILogger<WikiPlugin> logger,
    IHost host,
    ApplicationCommandService<ApplicationCommandContext> commandService,
    GatewayClient client
) : IPlugin
{
    public Task Start(CancellationToken cancellationToken)
    {
        host.AddApplicationCommandModule<WikiModule>();
        
        logger.LogInformation("Registered wiki commands");

        return Task.CompletedTask;
    }
}