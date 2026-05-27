using KnowledgeSystem.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Rest;

namespace KnowledgeSystem.Discord.Conversation;

public sealed class ConversationManager(
    ILogger<ConversationManager> logger,
    RestClient restClient,
    IServiceProvider serviceProvider
) : IConversationManager, IHostedService, IDisposable
{
    // Sorted by expiry timestamp for efficient eviction scanning.
    // Keyed by (ExpiresAt, ChannelId) to guarantee uniqueness.
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

    public ActiveConversation? TryGetConversation(ulong channelId)
    {
        lock (_lock)
        {
            return _byChannel.GetValueOrDefault(channelId);
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
            await conversation.CloseAsync(restClient, reason, cancellationToken);
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
                _byChannel.Remove(key.ChannelId);
                expired.Add(conversation);
            }
        }

        foreach (var conversation in expired)
        {
            logger.LogInformation("Evicting expired conversation in channel {channel}", conversation.ChannelId);
            await conversation.CloseAsync(restClient, "idle timeout", CancellationToken.None);
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

        await CloseAllAsync("application is shutting down", cancellationToken);
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
    
    public TLayer CreateConversation<TLayer>(ulong channelId, Func<IActiveConversation, TLayer> factory) where TLayer : IAgentMessagingLayer
    {
        lock (_lock)
        {
            if (_byChannel.ContainsKey(channelId))
            {
                throw new InvalidOperationException($"A conversation for channel {channelId} already exists.");
            }

            // Create the conversation first:
            var conversation = new ActiveConversation(
                serviceProvider.GetRequiredService<ILogger<ActiveConversation>>(),
                this,
                channelId
            );
            
            var layer = factory(conversation);
    
            // Then bind the dependency:
            conversation.Layer = layer;
            
            InsertSorted(conversation);

            return layer;
        }
    }
    
    #endregion
}