using KnowledgeSystem.Agent.Tools;
using KnowledgeSystem.Agents.Orchestration;
using OpenAI.Chat;

namespace KnowledgeSystem.Agent;

public sealed class ConversationalAgent : Agent<ConversationalContext>
{
    public ConversationalAgent(string agentId, IServiceProvider serviceProvider) : base(agentId)
    {
        Tools.FastContextToolHandler.Register(ToolRegistry, serviceProvider, new FastContextToolConfig());
        RepoFetchToolHandler.Register(ToolRegistry, serviceProvider, 16384);
        TreeToolHandler.Register(ToolRegistry, serviceProvider);
    }

    public override Task<AgentCompletionResult> HandleCompletion(ChatCompletion completion, ConversationalContext context)
    {
        context.InsertAssistantCompletion(completion);
        return Task.FromResult(Finish());
    }
}
