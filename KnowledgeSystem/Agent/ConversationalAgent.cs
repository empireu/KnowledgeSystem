using KnowledgeSystem.Agent.Events;
using KnowledgeSystem.Agent.Tools;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Events.Api;
using Microsoft.Extensions.AI;
using ChatOptions = KnowledgeSystem.Agent.Config.ChatOptions;

namespace KnowledgeSystem.Agent;

public sealed class ConversationalAgent : Agent<ConversationalContext>
{
    private readonly IEventManager _eventManager;

    public ConversationalAgent(IEventManager eventManager, string agentId, IServiceProvider serviceProvider, ChatOptions options) : base(agentId)
    {
        _eventManager = eventManager;
        
        FastContextToolHandler.Register(ToolRegistry, serviceProvider, new FastContextToolConfig());
        RepoFetchToolHandler.Register(ToolRegistry, serviceProvider, 8192);
        TreeToolHandler.Register(ToolRegistry, serviceProvider);
        
        if (options.Review != null)
        {
            PeerReviewSubAgentHandler.Register(ToolRegistry, options.Review);
        }
    }

    public override Task<AgentCallbackResult> HandleCompletion(AgentRunner<ConversationalContext> runner, ChatResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Text))
        {
            runner.ExecutionContext.ChatContext.InsertAssistant("I should write my final output now.");
            return Task.FromResult(AgentCallbackResult.Continue);
        }

        runner.ExecutionContext.InsertAssistantCompletion(response);
        return Task.FromResult(AgentCallbackResult.Break);
    }

    public override async Task<AgentCallbackResult> HandleToolFinish(AgentRunner<ConversationalContext> runner)
    {
        // P.S. alternatively, we can strong-link the handler. Maybe do
        if (runner.TryGetUniqueActiveSubAgentProxyForHandler<PeerReviewSubAgentHandler>(out var peerReviewProxy))
        {
            var proxy = (PeerReviewSubAgentHandler.Proxy)peerReviewProxy;

            if (proxy.ReviewContext.FinalStatus == PeerReviewContext.Status.Approved)
            {
                await _eventManager.SendAsync(new AgentPeerReviewedMessageEvent(proxy.ReviewContext.Report));
                
                return AgentCallbackResult.Break;
            }
        }
        
        return await base.HandleToolFinish(runner);
    }
}
