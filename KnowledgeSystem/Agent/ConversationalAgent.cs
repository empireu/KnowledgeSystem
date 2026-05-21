using KnowledgeSystem.Agent.Tools;
using KnowledgeSystem.Agents.Orchestration;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Agent;

public sealed class ConversationalAgent : Agent<ConversationalContext>
{
    public ConversationalAgent(string agentId, IServiceProvider serviceProvider, Discord.ChatOptions options) : base(agentId)
    {
        FastContextToolHandler.Register(ToolRegistry, serviceProvider, new FastContextToolConfig());
        RepoFetchToolHandler.Register(ToolRegistry, serviceProvider, 16384);
        TreeToolHandler.Register(ToolRegistry, serviceProvider);

        if (options.Review != null)
        {
            PeerReviewSubAgentHandler.Register(ToolRegistry, serviceProvider, options.Review);
        }
    }

    public override Task<AgentCallbackResult> HandleCompletion(AgentRunner<ConversationalContext> runner, ChatResponse response)
    {
        runner.ExecutionContext.InsertAssistantCompletion(response);
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
