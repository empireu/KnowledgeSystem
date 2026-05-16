using System.ClientModel;
using KnowledgeSystem.Agent;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Context.TokenEstimation;
using KnowledgeSystem.Agents.Helper;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Observer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace KnowledgeSystem.Discord.Conversation;

public sealed class ConversationManager : IConversationManager, IDisposable
{
    private readonly ILogger<ConversationManager> _logger;
    private readonly ChatClient _chatClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ChatOptions _chatOptions;
    private readonly string _systemPrompt;
 
    private readonly Dictionary<ulong, ActiveConversation> _conversations = [];
    
    private bool _disposed;

    public ConversationManager(
        ILogger<ConversationManager> logger,
        IServiceProvider serviceProvider, 
        IOptions<ChatOptions> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _chatOptions = options.Value;
        
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(_chatOptions.Endpoint),
        };

        var credentials = new ApiKeyCredential(_chatOptions.ApiKey);
        var client = new OpenAIClient(credentials, clientOptions);
        
        _chatClient = client.GetChatClient(_chatOptions.Model);

        TokenEstimator = TokenizerHelper.Create(new TokenizerInfo
        {
            ModelName = _chatOptions.Model,
            Kind = TokenizerKind.HuggingFace,
            ChatFormat = _chatOptions.Template,
            TokenizerDir = _chatOptions.TokenizerDir
        });
        
        _systemPrompt = File.ReadAllText(_chatOptions.SystemPromptFile);
    }

    public ChatCompletionOptions CreateOptionsForTurn(AgentRunner runner)
    {
        var result = new ExtendedChatCompletionOptions();

        if (!string.IsNullOrWhiteSpace(_chatOptions.ProviderOnly))
        {
            result.ProviderOnly = _chatOptions.ProviderOnly;
        }

        return result;
    }

    public ITokenEstimator TokenEstimator { get; }

    public bool HasConversation(ulong channelId) => _conversations.ContainsKey(channelId);

    public ActiveConversation GetChannelConversation(ulong channelId) => _conversations[channelId];

    public ActiveConversation? TryGetConversation(ulong channelId) => _conversations.GetValueOrDefault(channelId);
    
    public ActiveConversation CreateConversation(ulong channelId)
    {
        var context = new ConversationalContext();
        context.ChatContext.InsertSystem(_systemPrompt);

        var agent = new ConversationalAgent(channelId.ToString(), _serviceProvider);

        var conversation = new ActiveConversation(
            this,
            channelId,
            agent,
            context,
            _chatClient,
            _serviceProvider.GetRequiredService<ILogger<ActiveConversation>>()
        );
        
        _conversations[channelId] = conversation;

        return conversation;
    }

    public void RemoveConversation(ulong channelId)
    {
        if (_conversations.Remove(channelId, out var conversation))
        {
            conversation.Dispose();
        }
    }

    public async Task<string> AskAsync(string message, IAgentObserver observer, CancellationToken cancellationToken = default)
    {
        var context = new ConversationalContext();
        context.ChatContext.InsertSystem(_systemPrompt);
        context.ChatContext.InsertUser(message);

        var agent = new ConversationalAgent("ask", _serviceProvider);
       
        var runner = new AgentRunner<ConversationalContext>(
            observer,
            _chatClient,
            agent,
            parent: null,
            context,
            cancellationToken,
            completionFactory: this
        );

        try
        {
            await runner.RunAsync();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("One-shot query was cancelled");
            return "The request was cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "One-shot query failed with error");
            return "An error occurred.";
        }

        if (runner.FinishError != null)
        {
            _logger.LogWarning("One-shot query completed with error: {Error}", runner.FinishError);
            return $"Agent error: {runner.FinishError.Message}";
        }

        var lastAssistantMessage = context.ChatContext.Elements
            .OfType<ChatElement>()
            .Select(e => e.Message)
            .OfType<AssistantChatMessage>()
            .LastOrDefault();

        if (lastAssistantMessage != null)
        {
            var text = string.Join("\n", lastAssistantMessage.Content
                .Where(p => p.Kind == ChatMessageContentPartKind.Text)
                .Select(p => p.Text));

            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }
        }

        return "The agent completed but produced no text response.";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        
        _disposed = true;

        foreach (var conversation in _conversations.Values)
        {
            conversation.Dispose();
        }

        _conversations.Clear();
    }
}