using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Events.Api;
using KnowledgeSystem.Plugins.Library;
using KnowledgeSystem.Plugins.OreDb.AgentTools;
using KnowledgeSystem.Plugins.Wiki;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Plugins.OreDb;

public class WikiPluginIntegrationService(ILogger<WikiPluginIntegrationService> logger, WikiApi api, IServiceProvider serviceProvider) : IHostedService, IEventReceiver
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        api.Events.AddReceiver(this);

        logger.LogInformation("Added OreDb agent integration");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [SubscribeEvent]
    // ReSharper disable once UnusedMember.Global
    public void OnAgentCreate(WikiAgentCreateEvent @event)
    {
        RegisterTool(@event.Agent.ToolRegistry, QueryOreToolHandler.Register);
        RegisterTool(@event.Agent.ToolRegistry, ClosestOreToolHandler.Register);
        RegisterTool(@event.Agent.ToolRegistry, LargestOreToolHandler.Register);
        RegisterTool(@event.Agent.ToolRegistry, NearbyOresToolHandler.Register);
    }

    private void RegisterTool(AgentToolRegistry<BasicContext> registry, Action<AgentToolRegistry<BasicContext>, IServiceProvider> register)
    {
        try
        {
            register(registry, serviceProvider);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to register an OreDb agent tool. The wiki agent will run without it.");
        }
    }
}
