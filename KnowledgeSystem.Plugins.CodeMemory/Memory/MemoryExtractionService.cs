using System.Threading.Channels;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Ai;
using KnowledgeSystem.Events.Implementation;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeSystem.Plugins.CodeMemory.Memory;

public sealed class MemoryExtractionService : IHostedService
{
    private readonly CancellationTokenSource _cts = new();
    
    private readonly Channel<PendingStatement> _statements = Channel.CreateUnbounded<PendingStatement>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    private int _pendingChats;

    private readonly ILogger<MemoryExtractionService> _logger;
    private readonly MemorySystemConfig _memoryConfig;
    private readonly IServiceProvider _serviceProvider;
    private readonly IChatClient _extractionClient;
    private readonly string _systemPrompt;
    
    private Task? _processingTask;
    
    public MemoryExtractionService(ILogger<MemoryExtractionService> logger, IOptions<MemorySystemConfig> options, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _memoryConfig = options.Value;
        _serviceProvider = serviceProvider;
        _extractionClient = OpenAiChatClientFactory.Create(_memoryConfig.ExtractionProvider);
        _systemPrompt = File.ReadAllText(_memoryConfig.ExtractionSystemPrompt);
    }

    public async Task EnqueueStatementAsync(PendingStatement statement, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _pendingChats);
        await _statements.Writer.WriteAsync(statement, cancellationToken);
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _processingTask = ProcessStatementsAsync();
        
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();

        if (_processingTask != null)
        {
            try
            {
                await _processingTask;
            }
            catch (OperationCanceledException)
            {
                // Ignored
            }
        }
    }

    private async Task ProcessStatementsAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await _statements.Reader.WaitToReadAsync(_cts.Token);
                
                if (_statements.Reader.TryRead(out var chat))
                {
                    try
                    {
                        await ProcessStatementAsync(chat);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Memory extraction failed");
                    }

                    Interlocked.Decrement(ref _pendingChats);
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (_pendingChats > 0)
            {
                _logger.LogWarning("Stopped memory extraction with {count} dangling chats", _pendingChats);
            }
            else
            {
                _logger.LogInformation("Stopped memory extraction with no pending chats ");
            }
        }
    }

    private async Task ProcessStatementAsync(PendingStatement statement)
    {
        if (string.IsNullOrWhiteSpace(statement.Namespace))
        {
            _logger.LogWarning("Received statement with empty namespace");
            return;
        }

        if (string.IsNullOrWhiteSpace(statement.Content))
        {
            _logger.LogWarning("Received statement with empty content");
            return;
        }
        
        var extractionContext = new BasicContext();
        extractionContext.Timeline.InsertSystem(_systemPrompt);
        extractionContext.Timeline.InsertUser(statement.Content);

        var agent = new MemorySynthesisAgent(
            statement.Namespace,
            "memory_synthesis", 
            _serviceProvider
        );
        
        var runner = new AgentRunner<BasicContext>(
            _extractionClient, 
            agent,
            null,
            extractionContext,
            NullEventManager.Instance,
            _cts.Token,
            AgentRunner.ICompletionFactory.Wrap(_memoryConfig.Extraction.CreateOptions)
        );
        
        for (var turn = 0;; turn++)
        {
            if (turn == 50)
            {
                _logger.LogError(
                    "Hit {number} turns for memory extraction agent. Dropping memory from {startTime}!",
                    turn, 
                    statement.UtcSent
                );
                
                return;
            }
            
            var status = await runner.ExecuteTurn();

            if (status == AgentRunner.TurnStatus.CompletedWithError)
            {
                _logger.LogError(
                    "Memory extraction completed with error {error}. Dropping memory from {startTime}!",
                    runner.FinishError,
                    statement.UtcSent
                );
                
                return;
            }

            if (status == AgentRunner.TurnStatus.CompletedSuccessfully || _memoryConfig.RunForOneTurn)
            {
                _logger.LogInformation(
                    "Memory extraction for {utcStart} - {utcEnd} completed successfully",
                    statement.UtcSent,
                    statement.UtcSent
                );
                
                return;
            }
        }
    }
}