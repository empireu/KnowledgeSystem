using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Events.Api;
using KnowledgeSystem.Plugins.Library;
using KnowledgeSystem.Plugins.Wiki.Events;
using KnowledgeSystem.Plugins.Wiki.Tools.FastContext;
using KnowledgeSystem.Plugins.Wiki.Tools.FetchContext;
using KnowledgeSystem.Plugins.Wiki.Tools.FindFiles;
using KnowledgeSystem.Plugins.Wiki.Tools.GrepContent;
using KnowledgeSystem.Plugins.Wiki.Tools.ListDir;
using KnowledgeSystem.Plugins.Wiki.Tools.RepoFetch;
using KnowledgeSystem.Plugins.Wiki.Tools.Review;
using KnowledgeSystem.Retrieval.Api.Store;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Plugins.Wiki;

public sealed class ConversationalAgent : Agent<ConversationalContext>
{
    private readonly IEventManager _eventManager;

    public ConversationalAgent(IReadOnlyDocumentStore store, IEventManager eventManager, string agentId, IServiceProvider serviceProvider, WikiOptions options) : base(agentId)
    {
        _eventManager = eventManager;
        
        FastContextToolHandler.Register(
            ToolRegistry,
            store,
            serviceProvider,
            new FastContextToolConfig()
        );
        
        RepoFetchToolHandler.Register(
            ToolRegistry,
            store,
            serviceProvider,
            new RepoFetchToolConfig()
        );
        
        ListDirToolHandler.Register(
            ToolRegistry,
            store,
            serviceProvider,
            new ListDirToolConfig()
        );
        
        FindFilesToolHandler.Register(
            ToolRegistry,
            store,
            serviceProvider,
            new FindFilesToolConfig()
        );
        
        GrepContentToolHandler.Register(
            ToolRegistry, 
            store,
            serviceProvider,
            new GrepContentToolConfig()
        );
        
        FetchContextToolHandler.Register(
            ToolRegistry,
            store,
            serviceProvider,
            new FetchContextToolConfig()
        );
        
        if (options.ReviewProvider != null && options.ReviewSystemPromptFile != null)
        {
            PeerReviewSubAgentHandler.Register(
                ToolRegistry,
                options.ReviewProvider,
                options.Review,
                options.ReviewSystemPromptFile!
            );
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
