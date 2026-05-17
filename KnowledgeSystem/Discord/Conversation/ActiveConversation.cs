using KnowledgeSystem.Agent;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Observer;
using Microsoft.Extensions.Logging;
using NetCord.Rest;
using OpenAI.Chat;

namespace KnowledgeSystem.Discord.Conversation;

public sealed class ActiveConversation : IDisposable
{
    private static readonly TimeSpan TimeoutDuration = TimeSpan.FromHours(1);

    private readonly IConversationManager _manager;
    private readonly ConversationalAgent _agent;
    private readonly ConversationalContext _context;
    private readonly ContextCompactor _compactor;
    private readonly ChatClient _chatClient;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private CancellationTokenSource _runCts = new();
    private bool _disposed;

    public ulong ChannelId { get; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsRunning => _runLock.CurrentCount == 0;
    
    public ActiveConversation(IConversationManager manager, ulong channelId, ConversationalAgent agent, ConversationalContext context, ChatClient chatClient, ILogger<ActiveConversation> logger)
    {
        _manager = manager;
        ChannelId = channelId;
        _agent = agent;
        _context = context;
        _compactor = new ContextCompactor(manager.TokenEstimator, _chatClient);
        _chatClient = chatClient;
        _logger = logger;
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

    public async Task RunToCompletionAsync(string userMessage, IAgentObserver observer, CancellationToken cancellationToken = default)
    {
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

            var runner = new AgentRunner<ConversationalContext>(
                observer,
                _chatClient,
                _agent,
                parent: null,
                _context,
                token,
                completionFactory: _manager
            );

            try
            {
                await runner.RunAsync();
                var tokensBeforeCompaction = _manager.TokenEstimator.CountTokens(_context.ChatMessages);
                await _compactor.CompactAsync(_context.ChatContext, token);
                var tokensAfterCompaction = _manager.TokenEstimator.CountTokens(_context.ChatMessages);
                var delta = tokensBeforeCompaction - tokensAfterCompaction;
                _logger.LogInformation("Compacted {tokens} for conversation {channel}", delta, ChannelId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Conversation {channel} was cancelled", ChannelId);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Conversation {channel} failed with error", ChannelId);
                return;
            }

            if (runner.FinishError != null)
            {
                _logger.LogWarning("Conversation {channel} completed with error: {error}", ChannelId, runner.FinishError);
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
        if (_disposed) return;
        _disposed = true;

        _runCts.Cancel();
        _runCts.Dispose();
    }
}