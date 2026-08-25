using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Events.Api;
using KnowledgeSystem.Plugins.Library;
using KnowledgeSystem.Plugins.Library.Tools.FastContext;
using KnowledgeSystem.Plugins.Library.Tools.FetchContext;
using KnowledgeSystem.Plugins.Library.Tools.FindFiles;
using KnowledgeSystem.Plugins.Library.Tools.GrepContent;
using KnowledgeSystem.Plugins.Library.Tools.ListDir;
using KnowledgeSystem.Plugins.Library.Tools.RepoFetch;
using KnowledgeSystem.Plugins.Wiki.CodeRag;
using KnowledgeSystem.Plugins.Wiki.Memory;
using KnowledgeSystem.Plugins.Wiki.PeerReview;
using KnowledgeSystem.Retrieval.Api.Store;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Wiki.Wiki;

public sealed class WikiAgent : Agent<BasicContext>
{
    private readonly IEventManager _eventManager;
    
    public WikiAgent(IReadOnlyMarkdownDocumentStore store, IEventManager eventManager, string agentId, IServiceProvider serviceProvider, WikiOptions options) : base(agentId)
    {
        _eventManager = eventManager;

        var memoryStore = serviceProvider.GetService<MemoryStoreService>();

        if (memoryStore != null)
        {
            FetchMemoryToolHandler.Register(
                ToolRegistry,
                serviceProvider
            );
        }
        
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
        
        var codeReposFileSystem = serviceProvider.GetService<CodeReposFileSystem>();

        if (codeReposFileSystem != null)
        {
            CodeFindFilesToolHandler.Register(ToolRegistry, codeReposFileSystem, serviceProvider, new CodeFindFilesToolConfig());
            CodeReadFileToolHandler.Register(ToolRegistry, codeReposFileSystem, serviceProvider, new CodeReadFileToolConfig());
            // CodeGrepToolHandler.Register(ToolRegistry, codeReposFileSystem, serviceProvider, new CodeGrepToolConfig());
            CodeRipgrepToolHandler.Register(ToolRegistry, codeReposFileSystem, serviceProvider, new CodeRipgrepToolConfig());
        }

        if (options.CodeDllsDir != null)
        {
            CodeDotnetripToolHandler.Register(ToolRegistry, serviceProvider, new CodeDotnetripToolConfig { DllsDir = options.CodeDllsDir });
        }
        
        if (options is { ReviewProvider: not null, ReviewSystemPromptFile: not null })
        {
            PeerReviewSubAgentHandler.Register(
                ToolRegistry,
                options.ReviewProvider,
                options.Review,
                options.ReviewSystemPromptFile!
            );
        }
    }

    public override Task<AgentCallbackResult> HandleCompletion(AgentRunner<BasicContext> runner, ChatResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Text))
        {
            runner.ExecutionContext.Timeline.InsertAssistant("I should write my final output now.");
            return Task.FromResult(AgentCallbackResult.Continue);
        }

        runner.ExecutionContext.InsertAssistantCompletion(response);
        return Task.FromResult(AgentCallbackResult.Break);
    }

    public override async Task<AgentCallbackResult> HandleToolFinish(AgentRunner<BasicContext> runner)
    {
        if (runner.TryGetUniqueActiveSubAgentProxyForHandler<PeerReviewSubAgentHandler>(out var peerReviewProxy))
        {
            var proxy = (PeerReviewSubAgentHandler.Proxy)peerReviewProxy;

            if (proxy.ReviewContext.FinalStatus == PeerReviewContext.Status.Approved)
            {
                runner.ExecutionContext.Timeline.InsertElement(new ReviewedWikiResponseMarker
                {
                    VerifiedReport = proxy.ReviewContext.Report
                });
                
                await _eventManager.SendAsync(new AgentPeerReviewedMessageEvent(proxy.ReviewContext.Report));
                
                return AgentCallbackResult.Break;
            }
        }
        
        return await base.HandleToolFinish(runner);
    }
}
