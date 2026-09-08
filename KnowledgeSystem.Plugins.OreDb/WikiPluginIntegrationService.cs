using KnowledgeSystem.Events.Api;
using KnowledgeSystem.Plugins.Wiki;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Plugins.OreDb;

public class WikiPluginIntegrationService(ILogger<WikiPluginIntegrationService> logger, WikiApi api) : IHostedService, IEventReceiver
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        api.Events.AddReceiver(this);
        
        logger.LogInformation("Added OreDb agent integration");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [SubscribeEvent]
    public void OnAgentCreate(WikiAgentCreateEvent @event)
    {
        
    }
}