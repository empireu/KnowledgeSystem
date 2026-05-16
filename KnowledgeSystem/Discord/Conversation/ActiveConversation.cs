using KnowledgeSystem.Agent;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Observer;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace KnowledgeSystem.Discord.Conversation;

public sealed class ActiveConversation : IDisposable
{
    private readonly IConversationManager _manager;
    private readonly ulong _channelId;
    private readonly ConversationalAgent _agent;
    private readonly ConversationalContext _context;
    private readonly ContextCompactor _compactor;
    private readonly ChatClient _chatClient;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private CancellationTokenSource? _runCts;
    
    public ActiveConversation(IConversationManager manager, ulong channelId, ConversationalAgent agent, ConversationalContext context, ChatClient chatClient, ILogger<ActiveConversation> logger)
    {
        _manager = manager;
        _channelId = channelId;
        _agent = agent;
        _context = context;
        _compactor = new ContextCompactor(manager.TokenEstimator, _chatClient);
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task RunToCompletionAsync(string userMessage, IAgentObserver observer, CancellationToken cancellationToken = default)
    {
        await _runLock.WaitAsync(cancellationToken);
    
        try
        {
            _context.ChatContext.InsertUser(userMessage);

            var runner = new AgentRunner<ConversationalContext>(
                observer,
                _chatClient,
                _agent,
                parent: null,
                _context,
                cancellationToken,
                completionFactory: _manager
            );

            try
            {
                await runner.RunAsync();
                var tokensBeforeCompaction = _manager.TokenEstimator.CountTokens(_context.ChatMessages);
                await _compactor.CompactAsync(_context.ChatContext, cancellationToken);
                var tokensAfterCompaction = _manager.TokenEstimator.CountTokens(_context.ChatMessages);
                var delta = tokensBeforeCompaction - tokensAfterCompaction;
                _logger.LogInformation("Compacted {tokens} for conversation {channel}", delta, _channelId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Conversation {channel} was cancelled", _channelId);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Conversation {channel} failed with error", _channelId);
                return;
            }

            if (runner.FinishError != null)
            {
                _logger.LogWarning("Conversation {channel} completed with error: {error}", _channelId, runner.FinishError);
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
        _runCts?.Cancel();
    }

    public void Dispose()
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = null;
        _runLock.Dispose();
    }
}