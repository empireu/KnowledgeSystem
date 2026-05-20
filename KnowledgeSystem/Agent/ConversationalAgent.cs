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

    public override async Task<AgentCallbackResult> HandleToolFinish(AgentRunner<ConversationalContext> runner)
    {
        if (runner.TryGetUniqueActiveSubAgentProxyForHandler<PeerReviewSubAgentHandler>(out var peerReviewProxy))
        {
            var proxy = (PeerReviewSubAgentHandler.Proxy)peerReviewProxy;

            if (proxy.ReviewContext.FinalStatus == PeerReviewContext.Status.Approved)
            {
                await runner.Observer.OnAssistantMessageAsync(runner, proxy.ReviewContext.Report, runner.CancellationToken);
        
                return AgentCallbackResult.Break;
            }
        }
        
        return await base.HandleToolFinish(runner);
    }
}
