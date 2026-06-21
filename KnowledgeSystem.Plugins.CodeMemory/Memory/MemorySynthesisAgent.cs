using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Plugins.CodeMemory.Memory;

/// <summary>
///     Agent that distills a conversation transcript into structured memories.
/// </summary>
public sealed class MemorySynthesisAgent : Agent<BasicContext>
{
    public MemorySynthesisAgent(string @namespace, string agentId, IServiceProvider serviceProvider) : base(agentId)
    {
        SearchMemoriesToolHandler.Register(@namespace, ToolRegistry, serviceProvider);
        FetchMemoryToolHandler.Register(ToolRegistry, serviceProvider);
        CreateMemoryToolHandler.Register(@namespace, ToolRegistry, serviceProvider);
        DeleteMemoryToolHandler.Register(ToolRegistry, serviceProvider);
    }

    public override Task<AgentCallbackResult> HandleCompletion(AgentRunner<BasicContext> runner, ChatResponse response)
    {
        runner.ExecutionContext.InsertAssistantCompletion(response);
     
        return Task.FromResult(AgentCallbackResult.Break);
    }
}
