using KnowledgeSystem.Agent;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Discord.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Discord;

public class DiscordObserverFactory(IServiceProvider serviceProvider)
{
    public DiscordAgentIntegration CreateV2(AgentRunner<ConversationalContext> runner, IDiscordMessageTarget target)
    {
        return ActivatorUtilities.CreateInstance<DiscordAgentIntegration>(serviceProvider, runner, target);
    }
}