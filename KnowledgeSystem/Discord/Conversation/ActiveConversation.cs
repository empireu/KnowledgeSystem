using System.Diagnostics;
using KnowledgeSystem.Agent;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Discord.Integration;
using KnowledgeSystem.Telemetry;
using KnowledgeSystems.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NetCord.Rest;

namespace KnowledgeSystem.Discord.Conversation;

public sealed class ActiveConversation : IDisposable
{
    private static readonly TimeSpan TimeoutDuration = TimeSpan.FromHours(1);

    private readonly ILogger<ActiveConversation> _logger;
    private readonly IConversationManager _manager;
    private readonly ConversationalContext _context;
    private readonly ContextCompactor _compactor;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private CancellationTokenSource _runCts = new();
    private bool _disposed;

    public ulong ChannelId { get; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsRunning => _runLock.CurrentCount == 0;
    
    public ActiveConversation(
        ILogger<ActiveConversation> logger,
        IConversationManager manager,
        ulong channelId,
        ConversationalContext context,
        IChatClient chatClient
    ) 
    {
        _logger = logger;
        _manager = manager;
        ChannelId = channelId;
        _context = context;

        _compactor = new ContextCompactor(manager.TokenEstimator, chatClient);

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
        using var activity = KnowledgeSystemTelemetry.AgentChat.StartInternalActivity("ConversationMessage");
        activity?.SetTag("user_query", userMessage);
        
        ObjectDisposedException.ThrowIf(_disposed, this);
        TouchActivity();

        // If _runCts was cancelled (e.g. via Cancel()), recreate it so the conversation can be reused:
        if (_runCts.IsCancellationRequested)
        {
            _runCts.Dispose();
            _runCts = new CancellationTokenSource();
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_runCts.Token, cancellationToken);
        var token = linkedCts.Token;

        await _runLock.WaitAsync(token);
    
        try
        {
            _context.ChatContext.InsertUser(userMessage);

            var orchestration = _manager.CreateResponseOrchestrator(
                $"Channel {ChannelId}",
                _context,
                target,
                cancellationToken
            );
            try
            {
                var turns = await orchestration.RootRunner.RunAsync();
                var tokensBeforeCompaction = _manager.TokenEstimator.CountTokens(_context.ChatMessages);

                using (var compactionActivity = KnowledgeSystemTelemetry.AgentChat.StartInternalActivity("CompactConversation"))
                {
                    var compactionInfo = await _compactor.CompactAsync(_context.ChatContext, token);
                    
                    compactionActivity?.SetTag("tools_compacted", compactionInfo.ToolsCompacted);
                    compactionActivity?.SetTag("created_summary", compactionInfo.InvokedSummarizer);
                }
                
                var tokensAfterCompaction = _manager.TokenEstimator.CountTokens(_context.ChatMessages);
                var delta = tokensBeforeCompaction - tokensAfterCompaction;

                activity?.SetTag("turns", turns);
                activity?.SetTag("tokens_before_compaction", tokensBeforeCompaction);
                activity?.SetTag("tokens_after_compaction", tokensAfterCompaction);
                
                _logger.LogInformation("Compacted ~{tokens} for conversation {channel}", delta, ChannelId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Conversation {channel} was cancelled", ChannelId);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Conversation {channel} failed with error", ChannelId);
                activity?.SetTag("exception", ex.Message);
                activity?.SetStatus(ActivityStatusCode.Error);
                return;
            }

            if (orchestration.RootRunner.FinishError != null)
            {
                _logger.LogWarning("Conversation {channel} completed with error: {error}", ChannelId, orchestration.RootRunner.FinishError);
                activity?.SetTag("finish_error", orchestration.RootRunner.FinishError.Message);
                activity?.SetStatus(ActivityStatusCode.Error);
                return;
            }
            
            activity?.SetStatus(ActivityStatusCode.Ok);
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
