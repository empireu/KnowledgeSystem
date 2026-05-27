using KnowledgeSystem.Api;
using Microsoft.Extensions.Logging;
using NetCord.Rest;

namespace KnowledgeSystem.Discord.Conversation;

public sealed class ActiveConversation : IActiveConversation, IDisposable
{
    private static readonly TimeSpan TimeoutDuration = TimeSpan.FromHours(1);

    private readonly ILogger<ActiveConversation> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private CancellationTokenSource _runCts = new();
    private bool _disposed;

    private bool _prepared;

    /// <summary>
    ///     Set after the instance is created.
    /// </summary>
    public IAgentMessagingLayer Layer { get; }

    public ConversationScopeInfo ScopeInfo { get; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsRunning => _runLock.CurrentCount == 0;
    
    public ActiveConversation(ILogger<ActiveConversation> logger, ConversationScopeInfo scopeInfo, IAgentMessagingLayer layer) 
    {
        _logger = logger;
        ScopeInfo = scopeInfo;
        Layer = layer;
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
        await _runCts.CancelAsync();

        if (ScopeInfo.ScopeType == ConversationScopeInfo.Type.Channel)
        {
            try
            {
                await restClient.SendMessageAsync(ScopeInfo.Id, new MessageProperties
                {
                    Content = $"> Conversation ended: {reason}"
                }, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send close message for conversation {target}", ScopeInfo);
            }   
        }
    }

    public async Task RunToCompletionAsync(UserMessageInfo userMessage, IDiscordMessageTarget target, CancellationToken cancellationToken)
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

        if (!_prepared)
        {
            await Layer.PrepareAsync(cancellationToken);
            _prepared = true;
        }
        
        try
        {
            // The response pipeline is used to answer a single prompt:
            var pipeline = await Layer.CreateResponsePipeline(
                target,
                userMessage,
                cancellationToken
            );
            
            try
            {
                await pipeline.ExecuteAsync();
            }
            catch (OperationCanceledException)
            {
                await target.UpdateContentAsync("Interaction was cancelled", CancellationToken.None);
                _logger.LogInformation("Conversation {target} was cancelled", ScopeInfo);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Conversation {target} failed with exception", ScopeInfo);
                return;
            }
            
            _logger.LogInformation("Agent ran to completion");
        }
        finally
        {
            _runLock.Release();
        }
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
