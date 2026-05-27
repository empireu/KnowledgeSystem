using KnowledgeSystem.Api;
using KnowledgeSystem.Discord.Integration;
using Microsoft.Extensions.Logging;
using NetCord.Rest;

namespace KnowledgeSystem.Discord.Conversation;

public sealed class ActiveConversation : IActiveConversation, IDisposable
{
    private static readonly TimeSpan TimeoutDuration = TimeSpan.FromHours(1);

    private readonly ILogger<ActiveConversation> _logger;
    private readonly IConversationManager _manager;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private CancellationTokenSource _runCts = new();
    private bool _disposed;

    /// <summary>
    ///     Set after the instance is created.
    /// </summary>
    public IAgentMessagingLayer Layer { get; internal set; }

    public ulong ChannelId { get; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsRunning => _runLock.CurrentCount == 0;
    
    public ActiveConversation(ILogger<ActiveConversation> logger, ConversationManager manager, ulong channelId) 
    {
        _logger = logger;
        _manager = manager;
        ChannelId = channelId;

        // Circular dependency issue
        Layer = null!;
        
        TouchActivity();
    }

    /// <summary>
    ///     Resets the timeout for the conversation.
    /// </summary>
    public void TouchActivity()
    {
        ExpiresAt = DateTimeOffset.UtcNow + TimeoutDuration;
    }

    public async Task CloseAsync(RestClient restClient, string reason, CancellationToken cancellationToken = default)
    {
        Cancel();
        
        try
        {
            await restClient.SendMessageAsync(ChannelId, new MessageProperties
            {
                Content = $"> Conversation ended: {reason}"
            }, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send close message for conversation {channel}", ChannelId);
        }
    }

    public async Task RunToCompletionAsync(string userMessage, IDiscordMessageTarget target, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TouchActivity();

        // If _runCts was cancelled (e.g. via Cancel()), recreate it so the conversation can be reused:
        if (_runCts.IsCancellationRequested)
        {
            _runCts.Dispose();
            _runCts = new CancellationTokenSource();
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            _runCts.Token,
            cancellationToken
        );
        
        var token = linkedCts.Token;

        await _runLock.WaitAsync(token);
    
        try
        {
            // The response pipeline is used to answer a single prompt:
            var pipeline = await Layer.CreateResponsePipeline(
                target,
                new UserMessageInfo(userMessage),
                cancellationToken
            );
            
            try
            {
                await pipeline.ExecuteAsync();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Conversation {channel} was cancelled", ChannelId);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Conversation {channel} failed with exception", ChannelId);
                return;
            }
            
            _logger.LogInformation("Agent ran to completion");
        }
        finally
        {
            _runLock.Release();
        }
    }

    public void Cancel()
    {
        _runCts.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        
        _disposed = true;
        _runCts.Cancel();
        _runCts.Dispose();
    }
}
