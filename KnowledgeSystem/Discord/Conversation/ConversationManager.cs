using System.ClientModel;
using KnowledgeSystem.Agent;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Context.TokenEstimation;
using KnowledgeSystem.Agents.Helper;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Observer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord.Rest;
using OpenAI;
using OpenAI.Chat;

namespace KnowledgeSystem.Discord.Conversation;

public sealed class ConversationManager : IConversationManager, IHostedService, IDisposable
{
    private readonly ILogger<ConversationManager> _logger;
    private readonly ChatClient _chatClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ChatOptions _chatOptions;
    private readonly RestClient _restClient;
    private readonly string _systemPrompt;

    // Sorted by expiry timestamp for efficient eviction scanning.
    // Keyed by (ExpiresAt, ChannelId) to guarantee uniqueness.
    private readonly SortedList<(DateTimeOffset ExpiresAt, ulong ChannelId), ActiveConversation> _sorted = [];
    private readonly Dictionary<ulong, ActiveConversation> _byChannel = [];
    private readonly Lock _lock = new();
    
    private readonly PeriodicTimer _evictionTimer = new(TimeSpan.FromMinutes(1));
    private readonly CancellationTokenSource _evictionCts = new();
    private Task? _evictionLoop;

    private bool _disposed;

    public ConversationManager(
        ILogger<ConversationManager> logger,
        IServiceProvider serviceProvider,
        IOptions<ChatOptions> options,
        RestClient restClient)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _chatOptions = options.Value;
        _restClient = restClient;

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
            result.Temperature = _chatOptions.Temperature;
        }

        return result;
    }

    public ITokenEstimator TokenEstimator { get; }

    public bool HasConversation(ulong channelId)
    {
        lock (_lock)
        {
            return _byChannel.ContainsKey(channelId);
        }
    }

    public ActiveConversation GetChannelConversation(ulong channelId)
    {
        lock (_lock)
        {
            return _byChannel[channelId];
        }
    }

    public ActiveConversation? TryGetConversation(ulong channelId)
    {
        lock (_lock)
        {
            return _byChannel.GetValueOrDefault(channelId);
        }
    }

    public ActiveConversation CreateConversation(ulong channelId)
    {
        lock (_lock)
        {
            if (_byChannel.ContainsKey(channelId))
            {
                throw new InvalidOperationException($"A conversation for channel {channelId} already exists.");
            }

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

            InsertSorted(conversation);

            return conversation;
        }
    }

    public void RemoveConversation(ulong channelId)
    {
        ActiveConversation? conversation;
        lock (_lock)
        {
            conversation = RemoveFromSorted(channelId);
        }
        conversation?.Dispose();
    }

    public void TouchConversation(ulong channelId)
    {
        lock (_lock)
        {
            if (_byChannel.TryGetValue(channelId, out var conversation))
            {
                RemoveFromSorted(channelId);
                conversation.TouchActivity();
                InsertSorted(conversation);
            }
        }
    }

    public async Task CloseAllAsync(string reason, CancellationToken cancellationToken = default)
    {
        List<ActiveConversation> conversations;
        lock (_lock)
        {
            conversations = [.._byChannel.Values];
        }

        foreach (var conversation in conversations)
        {
            await conversation.CloseAsync(_restClient, reason, cancellationToken);
            conversation.Dispose();
        }
        
        _logger.LogInformation("Closed {count} conversations", conversations.Count);

        lock (_lock)
        {
            _sorted.Clear();
            _byChannel.Clear();
        }
    }

    private void InsertSorted(ActiveConversation conversation)
    {
        _sorted[(conversation.ExpiresAt, conversation.ChannelId)] = conversation;
        _byChannel[conversation.ChannelId] = conversation;
    }

    private ActiveConversation? RemoveFromSorted(ulong channelId)
    {
        if (!_byChannel.Remove(channelId, out var conversation))
            return null;

        // Find the sorted entry by channelId (expiry may have changed since insert)
        var key = _sorted.Keys.FirstOrDefault(k => k.ChannelId == channelId);
        _sorted.Remove(key);
        return conversation;
    }

    private async Task EvictionLoopAsync()
    {
        while (await _evictionTimer.WaitForNextTickAsync(_evictionCts.Token))
        {
            await EvictExpiredAsync();
        }
    }

    private async Task EvictExpiredAsync()
    {
        var now = DateTimeOffset.UtcNow;
        List<ActiveConversation> expired = [];

        lock (_lock)
        {
            while (_sorted.Count > 0 && _sorted.Keys[0].ExpiresAt <= now)
            {
                var key = _sorted.Keys[0];
                var conversation = _sorted.Values[0];
                _sorted.RemoveAt(0);
                _byChannel.Remove(key.ChannelId);
                expired.Add(conversation);
            }
        }

        foreach (var conversation in expired)
        {
            _logger.LogInformation("Evicting expired conversation in channel {channel}", conversation.ChannelId);
            await conversation.CloseAsync(_restClient, "idle timeout", CancellationToken.None);
            conversation.Dispose();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _evictionLoop = EvictionLoopAsync();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _evictionCts.CancelAsync();
        if (_evictionLoop != null)
        {
            try
            {
                await _evictionLoop;
            }
            catch (OperationCanceledException)
            {
                // Ignored
            }
        }

        await CloseAllAsync("application is shutting down", cancellationToken);
    }

    public async Task AskAsync(string message, IAgentObserver observer, CancellationToken cancellationToken = default)
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
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "One-shot query failed with error");
            return;
        }

        if (runner.FinishError != null)
        {
            _logger.LogWarning("One-shot query completed with error: {Error}", runner.FinishError);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        
        _disposed = true;

        _evictionCts.Cancel();
        _evictionTimer.Dispose();
        _evictionCts.Dispose();

        lock (_lock)
        {
            foreach (var conversation in _byChannel.Values)
            {
                conversation.Dispose();
            }

            _sorted.Clear();
            _byChannel.Clear();
        }
    }
}