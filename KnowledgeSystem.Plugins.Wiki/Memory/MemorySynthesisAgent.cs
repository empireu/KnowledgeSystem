using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Plugins.Wiki.Memory;

/// <summary>
///     Agent that distills a conversation transcript into structured memories.
/// </summary>
public sealed class MemorySynthesisAgent : Agent<BasicContext>
{
    public MemorySynthesisAgent(string agentId, IServiceProvider serviceProvider) : base(agentId)
    {
        CreateMemoryToolHandler.Register(ToolRegistry, serviceProvider);
    }

    public override Task<AgentCallbackResult> HandleCompletion(AgentRunner<BasicContext> runner, ChatResponse response)
    {
        runner.ExecutionContext.InsertAssistantCompletion(response);
     
        return Task.FromResult(AgentCallbackResult.Break);
    }
}
