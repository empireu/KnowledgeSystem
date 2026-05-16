using KnowledgeSystem.Agents.Orchestration;
using OpenAI.Chat;

namespace KnowledgeSystem.Agent;

public sealed class ConversationalAgent : Agent<ConversationalContext>
{
    public ConversationalAgent(string agentId, IServiceProvider serviceProvider) : base(agentId)
    {
        FastContextToolHandler.Register(ToolRegistry, serviceProvider, 16384);
        RepoFetchToolHandler.Register(ToolRegistry, serviceProvider, 16384);
        TreeToolHandler.Register(ToolRegistry, serviceProvider);
    }

    public override Task<AgentCompletionResult> HandleCompletion(ChatCompletion completion, ConversationalContext context)
    {
        context.InsertAssistantCompletion(completion);
        return Task.FromResult(Finish());
    }
}
