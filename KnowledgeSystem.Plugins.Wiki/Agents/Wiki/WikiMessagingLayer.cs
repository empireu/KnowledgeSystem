using KnowledgeSystem.Agents.Context.TokenEstimation;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Ai;
using KnowledgeSystem.Api;
using KnowledgeSystem.Events.Api;
using KnowledgeSystem.Plugins.Library;
using KnowledgeSystem.Plugins.Wiki.Agents.PeerReview;
using KnowledgeSystem.Plugins.Wiki.Memory;
using KnowledgeSystem.Retrieval.Api.Store;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeSystem.Plugins.Wiki.Agents.Wiki;

/// <summary>
///     Messaging layer for one-shot ask and long-running conversations with the wiki agent.
/// </summary>
public sealed class WikiMessagingLayer : IAgentMessagingLayer
{
    private readonly BasicContext _context = new();
    private readonly ILogger<WikiMessagingLayer> _logger;
    private readonly IReadOnlyDocumentStore _store;
    private readonly string _name;
    private readonly WikiOptions _config;
    private readonly IServiceProvider _serviceProvider;
    
    private readonly IChatClient _chatClient;
    private readonly ITokenEstimator _tokenEstimator;

    private readonly IMemoryExtractionService? _memoryExtractionService;

    private DateTime _utcStart;
    
    public WikiMessagingLayer(
        ILogger<WikiMessagingLayer> logger,
        WikiStores stores,
        string name,
        IOptions<WikiOptions> configOptions,
        IServiceProvider serviceProvider
        )
    {
        _logger = logger;
        _store = stores.Store;
        _name = name;
        _config = configOptions.Value;
        _serviceProvider = serviceProvider;
        
        _chatClient = OpenAiChatClientFactory.Create(_config.ChatProvider);

        _tokenEstimator = BasicTokenEstimator.Create(new BasicTokenEstimatorConfig
        {
            ModelName = _config.ChatProvider.Model,
            Kind = TokenizerKind.HuggingFace,
            ChatFormat = _config.Template,
            TokenizerDir = _config.TokenizerDir
        });
        
        _memoryExtractionService = serviceProvider.GetService<IMemoryExtractionService>();
    }

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        var systemPrompt = await File.ReadAllTextAsync(_config.SystemPromptFile, cancellationToken);
        _logger.LogInformation("Loaded system prompt {hash}", systemPrompt.GetHashCode().ToString("X"));
        _context.Timeline.InsertSystem(systemPrompt);
        
        _utcStart = DateTime.UtcNow;
    }

    public Task<IResponsePipeline> CreateResponsePipeline(IDiscordMessageTarget messageTarget, UserMessageInfo userMessageInfo, CancellationToken cancellationToken)
    {
        _context.Timeline.InsertUser($"{userMessageInfo.Username}: {userMessageInfo.Message}");
        
        // Creates the event manager, used by the agent's orchestration logic:
        var eventManager = ActivatorUtilities.CreateInstance<AgentEventManager>(_serviceProvider);
        
        // Orchestrates all high-level events and sub-agents.
        // Uses the event manager to dispatch the final output event, after review rewrite:
        var agent = new WikiAgent(
            _store,
            eventManager,
            _name,
            _serviceProvider,
            _config
        );
        
        var runner = new AgentRunner<BasicContext>(
            client: _chatClient,
            agent: agent,
            parent: null,
            context: _context,
            eventManager: eventManager,
            cancellationToken: cancellationToken,
            completionFactory: AgentRunner.ICompletionFactory.Wrap(_config.Chat.CreateOptions)
        );

        // Handles the base discord integration:
        var observer = ActivatorUtilities.CreateInstance<DiscordMessageIntegration>(
            _serviceProvider,
            runner,
            messageTarget,
            _tokenEstimator
        );
        
        // Handles the special review agent message flow:
        var router = new PeerReviewRouter(observer);
        
        // Links the data flow from the agents:
        eventManager.AddReceiver(observer);
        eventManager.AddReceiver(router);

        IResponsePipeline pipeline = ActivatorUtilities.CreateInstance<AssembledResponsePipeline>(
            _serviceProvider,
            observer,
            runner
        );

        return Task.FromResult(pipeline);
    }
    
    /// <summary>
    ///     Routes the custom message endpoints to the discord renderer.
    /// </summary>
    private sealed class PeerReviewRouter(DiscordMessageIntegration integration) : IEventReceiver
    {
        /// <summary>
        ///     The peer review agent outputs one of these events. The content needs to be presented to the user.
        ///     In the context, it exists as the parameters of the tool call the main agent did to invoke the review agent.
        ///     Instead of adding a synthetic completion to the timeline and sending the base chat event, or rewriting the history in the vicinity of the message, we pipe it like this into the discord integration.
        ///     This is the most cache-friendly approach and it's clean in terms of what the LLM sees.
        /// </summary>
        [SubscribeEvent]
        // ReSharper disable once UnusedMember.Local
        public async ValueTask OnReviewedMessageAsync(AgentPeerReviewedMessageEvent @event, CancellationToken cancellationToken)
        {
            await integration.PresentMessage(
                @event.Content,
                [new KeyValuePair<string, string>("Reviewed", "Yes")],  
                cancellationToken
            );
        }
    }

    private sealed class AssembledResponsePipeline(ILogger<AssembledResponsePipeline> logger, DiscordMessageIntegration integration, AgentRunner<BasicContext> runner) : IResponsePipeline
    {
        public async Task ExecuteAsync()
        {
            var turns = await runner.RunAsync();

            if (runner.FinishError != null)
            {
                logger.LogError(
                    "Wiki response pipeline on {integration} failed with {error} after {turns} turns",
                    integration,
                    runner.FinishError,
                    turns
                );
            }
        }
    }

    public async Task CloseAsync(LayerCloseReason reason, CancellationToken cancellationToken)
    {
        if (_memoryExtractionService != null && reason is LayerCloseReason.ConversationTimeout or LayerCloseReason.ConversationEnded)
        {
            await _memoryExtractionService.EnqueueChatAsync(new PendingChat(_context, _utcStart, DateTime.UtcNow), cancellationToken);
        }
    }
}