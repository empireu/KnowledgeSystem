using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Plugins.Surveillance.Agent;

public sealed class KnowledgeGraphAgent : Agent<BasicContext>
{
    public KnowledgeGraphAgent(string agentId, IServiceProvider serviceProvider) : base(agentId)
    {
        DbStatusToolHandler.Register(ToolRegistry, serviceProvider);
        SearchEntitiesToolHandler.Register(ToolRegistry, serviceProvider);
        SearchClaimsToolHandler.Register(ToolRegistry, serviceProvider);
        GetEntityDetailsToolHandler.Register(ToolRegistry, serviceProvider);
    }

    public override Task<AgentCallbackResult> HandleCompletion(AgentRunner<BasicContext> runner, ChatResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Text))
        {
            runner.ExecutionContext.Timeline.InsertAssistant("I should write my final output now.");
            return Task.FromResult(AgentCallbackResult.Continue);
        }

        runner.ExecutionContext.InsertAssistantCompletion(response);
        return Task.FromResult(AgentCallbackResult.Break);
    }
}
