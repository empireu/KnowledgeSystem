using KnowledgeSystem.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Rest;

namespace KnowledgeSystem.Discord.Conversation;

public sealed class ConversationManager(
    ILogger<ConversationManager> logger,
    RestClient restClient,
    ResponseTracker tracker,
    IServiceProvider serviceProvider
) : IConversationManager, IHostedService, IDisposable
{
    private static readonly TimeSpan AgentTimeout = TimeSpan.FromMinutes(14);
    
    private readonly SortedList<(DateTimeOffset ExpiresAt, ulong ChannelId), ActiveConversation> _sorted = [];
    private readonly Dictionary<ulong, ActiveConversation> _byChannel = [];
    private readonly Lock _lock = new();
    
    private readonly PeriodicTimer _evictionTimer = new(TimeSpan.FromMinutes(1));
    private readonly CancellationTokenSource _evictionCts = new();
    private Task? _evictionLoop;

    private bool _disposed;

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

    public ActiveConversation? TryGetAndTouchConversation(ulong channelId)
    {
        lock (_lock)
        {
            var result = _byChannel.GetValueOrDefault(channelId);
            result?.TouchActivity();
            return result;
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

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        List<ActiveConversation> conversations;
        lock (_lock)
        {
            conversations = [.._byChannel.Values];
        }

        foreach (var conversation in conversations)
        {
            await conversation.CloseAsync(restClient, LayerCloseReason.Shutdown, "Shutting down", cancellationToken);
            conversation.Dispose();
        }
        
        logger.LogInformation("Closed {count} conversations", conversations.Count);

        lock (_lock)
        {
            _sorted.Clear();
            _byChannel.Clear();
        }
    }

    private void InsertSorted(ActiveConversation conversation)
    {
        if (conversation.ScopeInfo.ScopeType != ConversationScopeInfo.Type.Channel)
        {
            throw new Exception($"Cannot insert conversation of type {conversation.ScopeInfo.ScopeType}");
        }
        
        // From parent, but for safety:
        lock (_lock)
        {
            _sorted[(conversation.ExpiresAt, conversation.ScopeInfo.Id)] = conversation;
            _byChannel[conversation.ScopeInfo.Id] = conversation;
        }
    }

    private ActiveConversation? RemoveFromSorted(ulong channelId)
    {
        if (!_byChannel.Remove(channelId, out var conversation))
        {
            return null;
        }

        // Find the sorted entry by channelId (expiry may have changed since insert)
        var key = _sorted.Keys.FirstOrDefault(k => k.ChannelId == channelId);
        _sorted.Remove(key);
        return conversation;
    }

    #region Eviction
    
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
                
                if (conversation.ExpiresAt <= now)
                {
                    _byChannel.Remove(key.ChannelId);
                    expired.Add(conversation);
                }
                else
                {
                    _sorted[(conversation.ExpiresAt, conversation.ScopeInfo.Id)] = conversation;
                }
            }
        }

        foreach (var conversation in expired)
        {
            logger.LogInformation("Evicting expired conversation in channel {channel}", conversation.ScopeInfo.Id);
            await conversation.CloseAsync(restClient, LayerCloseReason.ConversationTimeout, "Inactive", CancellationToken.None);
            conversation.Dispose();
        }
    }

    #endregion

    #region Lifetime

    /// <summary>
    ///     Starts the eviction loop in the background.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _evictionLoop = EvictionLoopAsync();
        
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Cancels the eviction loop and closes all conversations.
    /// </summary>
    /// <param name="cancellationToken"></param>
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

        await ShutdownAsync(cancellationToken);
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

    #endregion

    #region API

    public void OpenConversation(ulong channel, IAgentMessagingLayer layer)
    {
        lock (_lock)
        {
            if (_byChannel.ContainsKey(channel))
            {
                throw new InvalidOperationException($"A conversation for channel {channel} already exists.");
            }

            var conversation = new ActiveConversation(
                serviceProvider.GetRequiredService<ILogger<ActiveConversation>>(),
                new ConversationScopeInfo(channel, ConversationScopeInfo.Type.Channel),
                layer
            );
         
            InsertSorted(conversation);
        }
    }

    public void RunOneShotConversation(ulong interactionId, UserMessageInfo userMessageInfo, IDiscordMessageTarget target, IAgentMessagingLayer layer)
    {
        var conversation = new ActiveConversation(
            serviceProvider.GetRequiredService<ILogger<ActiveConversation>>(),
            new ConversationScopeInfo(interactionId, ConversationScopeInfo.Type.Single),
            layer
        );
        
        var cts = new CancellationTokenSource(AgentTimeout);
        tracker.Add(interactionId, new ActiveRunInfo
        {
            Cts = cts
        });
        
        var task = conversation.RunToCompletionAsync(userMessageInfo, target, cts.Token);

        _ = ObserveExecutionAndFinishRun(
            task,
            target,
            conversation.ScopeInfo,
            cts.Token,
            conversation
        );
    }

    #endregion

    internal async Task HandleExternalMessage(Message message) 
    {
        // Only handle messages in active conversation threads:
        var conversation = TryGetAndTouchConversation(message.ChannelId);
        
        if (conversation == null)
        {
            return;
        }

        if (conversation.IsRunning)
        {
            await restClient.SendMessageAsync(message.ChannelId, new MessageProperties
            {
                Content = "> Another operation is in progress."
            });
                
            return;
        }

        logger.LogInformation(
            "Processing message in conversation {channel} from {user}: {content}",
            message.ChannelId,
            message.Author.Username,
            message.Content
        );

        var statusMessage = await restClient.SendMessageAsync(message.ChannelId, new MessageProperties
        {
            Content = "> *Processing...*"
        });
            
        var target = new ChannelMessageTarget(
            restClient,
            message.ChannelId,
            statusMessage.Id
        );
        
        var cts = new CancellationTokenSource(AgentTimeout);
        tracker.Add(conversation.ScopeInfo.Id, new ActiveRunInfo
        {
            Cts = cts
        });
        
        var task = conversation.RunToCompletionAsync(
            new UserMessageInfo(message.Content, message.Author.Username),
            target,
            cts.Token
        );
        
        _ = ObserveExecutionAndFinishRun(
            task,
            target,
            conversation.ScopeInfo,
            cts.Token
        );
    }
    
    /// <summary>
    ///     Runs in the background. Observes the execution of the agent's task, logs errors, and removes the run from the active tracker.
    /// </summary>
    private async Task ObserveExecutionAndFinishRun(Task agentTask, IDiscordMessageTarget target, ConversationScopeInfo scopeInfo, CancellationToken cancellationToken, ActiveConversation? conversationToCleanup = null)
    {
        try
        {
            await agentTask;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Agent task was cancelled for run {scope}", scopeInfo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent task for run {scope} threw an exception", scopeInfo);

            if (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await target.UpdateContentAsync($"An error occurred: {ex.Message}", cancellationToken);
                }
                catch (Exception discordEx)
                {
                    logger.LogError(discordEx, "Discord communication error for run {scope}", scopeInfo);
                }   
            }
        }
        finally
        {
            tracker.Remove(scopeInfo.Id);
            
            if (conversationToCleanup != null)
            {
                await conversationToCleanup.CloseAsync(restClient, LayerCloseReason.ConversationEnded, "One-shot complete", CancellationToken.None);
                conversationToCleanup.Dispose();
            }
        }
    }
}