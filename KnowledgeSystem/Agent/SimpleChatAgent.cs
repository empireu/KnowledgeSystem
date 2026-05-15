using KnowledgeSystem.Agents.Orchestration;
using OpenAI.Chat;

namespace KnowledgeSystem.Agent;

public sealed class SimpleChatAgent : Agent<SimpleChatContext>
{
    public SimpleChatAgent(string agentId, IServiceProvider serviceProvider) : base(agentId)
    {
        FastContextToolHandler.Register(ToolRegistry, serviceProvider);
        TreeToolHandler.Register(ToolRegistry, serviceProvider);
    }

    public override Task<AgentCompletionResult> HandleCompletion(ChatCompletion completion, SimpleChatContext context)
    {
        return Task.FromResult(Finish());
    }
}
