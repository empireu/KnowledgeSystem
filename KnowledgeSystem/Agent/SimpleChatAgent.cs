using KnowledgeSystem.Agents;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using OpenAI.Chat;

namespace KnowledgeSystem.Agent;

public sealed class SimpleChatAgent : Agent<SimpleChatContext, AgentVoidResult>
{
    public SimpleChatAgent(string agentId, IServiceProvider serviceProvider) : base(agentId)
    {
        FastContextToolHandler.Register(ToolRegistry, serviceProvider);
    }

    public override Task<AgentCompletionResult<AgentVoidResult>> CompleteAsync(ChatCompletion completion, SimpleChatContext context)
    {
        // A simple chat agent always finishes after a text response
        return Task.FromResult(new AgentCompletionResult<AgentVoidResult>(
            finishesAgent: true,
            result: AgentVoidResult.Instance
        ));
    }
}
