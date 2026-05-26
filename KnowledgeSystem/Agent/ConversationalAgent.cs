using KnowledgeSystem.Agent.Config;
using KnowledgeSystem.Agent.Events;
using KnowledgeSystem.Agent.Tools.FastContext;
using KnowledgeSystem.Agent.Tools.FetchContext;
using KnowledgeSystem.Agent.Tools.FindFiles;
using KnowledgeSystem.Agent.Tools.GrepContent;
using KnowledgeSystem.Agent.Tools.ListDir;
using KnowledgeSystem.Agent.Tools.RepoFetch;
using KnowledgeSystem.Agent.Tools.Review;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Events.Api;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Agent;

public sealed class ConversationalAgent : Agent<ConversationalContext>
{
    private readonly IEventManager _eventManager;

    public ConversationalAgent(IEventManager eventManager, string agentId, IServiceProvider serviceProvider, ApplicationOptions options) : base(agentId)
    {
        _eventManager = eventManager;
        
        FastContextToolHandler.Register(ToolRegistry, serviceProvider, new FastContextToolConfig());
        RepoFetchToolHandler.Register(ToolRegistry, serviceProvider, new RepoFetchToolConfig());
        ListDirToolHandler.Register(ToolRegistry, serviceProvider, new ListDirToolConfig());
        FindFilesToolHandler.Register(ToolRegistry, serviceProvider, new FindFilesToolConfig());
        GrepContentToolHandler.Register(ToolRegistry, serviceProvider, new GrepContentToolConfig());
        FetchContextToolHandler.Register(ToolRegistry, serviceProvider, new FetchContextToolConfig());
        
        if (options.ReviewProvider != null)
        {
            PeerReviewSubAgentHandler.Register(ToolRegistry, options.ReviewProvider, options.ReviewChat, options.ReviewSystemPromptFile!);
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
