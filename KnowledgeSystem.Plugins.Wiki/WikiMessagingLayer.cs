using KnowledgeSystem.Agents.Context.TokenEstimation;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Ai;
using KnowledgeSystem.Api;
using KnowledgeSystem.Events.Api;
using KnowledgeSystem.Plugins.Library;
using KnowledgeSystem.Plugins.Wiki.Events;
using KnowledgeSystem.Retrieval.Api.Store;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeSystem.Plugins.Wiki;

/// <summary>
///     Messaging layer for one-shot ask and long-running conversations with the wiki agent.
/// </summary>
public sealed class WikiMessagingLayer : IAgentMessagingLayer
{
    private readonly ConversationalContext _context = new();
    private readonly ILogger<WikiMessagingLayer> _logger;
    private readonly IReadOnlyDocumentStore _store;
    private readonly string _name;
    private readonly ApplicationOptions _config;
    private readonly IServiceProvider _serviceProvider;
    
    private readonly IChatClient _chatClient;
    private readonly ITokenEstimator _tokenEstimator;
    
    public WikiMessagingLayer(
        ILogger<WikiMessagingLayer> logger,
        IReadOnlyDocumentStore store,
        string name,
        IOptions<ApplicationOptions> configOptions,
        IServiceProvider serviceProvider
        )
    {
        _logger = logger;
        _store = store;
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
    }

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        var systemPrompt = await File.ReadAllTextAsync(_config.SystemPromptFile, cancellationToken);
        _logger.LogInformation("Loaded system prompt {hash}", systemPrompt.GetHashCode().ToString("X"));
        _context.ChatContext.InsertSystem(systemPrompt);
    }

    public Task<IResponsePipeline> CreateResponsePipeline(IDiscordMessageTarget messageTarget, UserMessageInfo userMessageInfo, CancellationToken cancellationToken)
    {
        // Creates the event manager, used by the agent's orchestration logic:
        var eventManager = ActivatorUtilities.CreateInstance<AgentEventManager>(_serviceProvider);
        
        // Orchestrates all high-level events and sub-agents.
        // Uses the event manager to dispatch the final output event, after review rewrite:
        var agent = new ConversationalAgent(
            _store,
            eventManager,
            _name,
            _serviceProvider,
            _config
        );
        
        var runner = new AgentRunner<ConversationalContext>(
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
            messageTarget,
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

    private sealed class AssembledResponsePipeline(ILogger<AssembledResponsePipeline> logger, DiscordMessageIntegration integration, AgentRunner<ConversationalContext> runner) : IResponsePipeline
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
}