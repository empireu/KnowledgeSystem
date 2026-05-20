using KnowledgeSystem.Agent.Tools;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Discord;
using OpenAI.Chat;

namespace KnowledgeSystem.Agent;

public sealed class ConversationalAgent : Agent<ConversationalContext>
{
    public ConversationalAgent(string agentId, IServiceProvider serviceProvider, ChatOptions options) : base(agentId)
    {
        FastContextToolHandler.Register(ToolRegistry, serviceProvider, new FastContextToolConfig());
        RepoFetchToolHandler.Register(ToolRegistry, serviceProvider, 16384);
        TreeToolHandler.Register(ToolRegistry, serviceProvider);

        if (options.Review != null)
        {
            PeerReviewSubAgentHandler.Register(ToolRegistry, options.Review);
        }
    }

    public override Task<AgentCallbackResult> HandleCompletion(AgentRunner<ConversationalContext> runner, ChatCompletion completion)
    {
        runner.ExecutionContext.InsertAssistantCompletion(completion);
        return Task.FromResult(AgentCallbackResult.Break);
    }

    public override Task<AgentCallbackResult> HandleToolFinish(AgentRunner<ConversationalContext> runner)
    {
        if (runner.TryGetUniqueActiveSubAgentProxyForHandler<PeerReviewSubAgentHandler>(out var peerReviewProxy))
        {
            return Task.FromResult(StitchPeerReviewOutput(runner.ExecutionContext, (PeerReviewSubAgentHandler.Proxy)peerReviewProxy));
        }
        
        return base.HandleToolFinish(runner);
    }

    private AgentCallbackResult StitchPeerReviewOutput(ConversationalContext mainAgentContext, PeerReviewSubAgentHandler.Proxy proxy)
    {
        
    }
}
