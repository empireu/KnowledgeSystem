using System.Text;
using System.Threading.Channels;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Ai;
using KnowledgeSystem.Events.Implementation;
using KnowledgeSystem.Plugins.Library;
using KnowledgeSystem.Plugins.Wiki.Agents.Wiki;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeSystem.Plugins.Wiki.Memory;

public sealed class MemoryExtractionService : IMemoryExtractionService, IHostedService
{
    private readonly CancellationTokenSource _cts = new();
    
    private readonly Channel<PendingChat> _chats = Channel.CreateUnbounded<PendingChat>(new UnboundedChannelOptions
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
    
    
    public MemoryExtractionService(ILogger<MemoryExtractionService> logger, IOptions<WikiOptions> options, IServiceProvider serviceProvider)
    {
        _logger = logger;
        var config = options.Value;
        _memoryConfig = config.Memory ?? throw new InvalidOperationException("Created memory service, but the config is null");
        _serviceProvider = serviceProvider;
        _extractionClient = OpenAiChatClientFactory.Create(_memoryConfig.ExtractionProvider);
        _systemPrompt = File.ReadAllText(config.Memory.ExtractionSystemPrompt);
    }

    public async Task EnqueueChatAsync(PendingChat chat, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _pendingChats);
        await _chats.Writer.WriteAsync(chat, cancellationToken);
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _processingTask = ProcessConversationsAsync();
        
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

    private async Task ProcessConversationsAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await _chats.Reader.WaitToReadAsync(_cts.Token);
                
                if (_chats.Reader.TryRead(out var chat))
                {
                    try
                    {
                        await ProcessChatAsync(chat);
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

    private async Task ProcessChatAsync(PendingChat chat)
    {
        var sb = new StringBuilder();

        var hasUser = false;
        var hasAssistant = false;
        
        foreach (var element in chat.Context.Timeline.Elements)
        {
            switch (element)
            {
                case ChatElement chatElement:
                {
                    if (chatElement.Message.Role == ChatRole.User)
                    {
                        if (!string.IsNullOrWhiteSpace(chatElement.Message.Text))
                        {
                            sb.AppendLine($"({ChatRole.User.Value}) {chatElement.Message.Text}");
                            hasUser = true;
                        }
                    }
                    else if (chatElement.Message.Role == ChatRole.Assistant)
                    {
                        if (!string.IsNullOrWhiteSpace(chatElement.Message.Text))
                        {
                            sb.AppendLine($"({ChatRole.Assistant.Value}) {chatElement.Message.Text}");
                            hasAssistant = true;   
                        }
                    }
                    
                    break;
                }
                case ReviewedWikiResponseMarker reviewedResponse:
                {
                    if (!string.IsNullOrWhiteSpace(reviewedResponse.VerifiedReport))
                    {
                        sb.AppendLine($"({ChatRole.Assistant.Value}) {reviewedResponse.VerifiedReport}");
                        hasAssistant = true;
                    }   
                    
                    break;
                }
            }
        }

        if (!hasUser || !hasAssistant)
        {
            _logger.LogWarning("Received conversation that doesn't have both user and assistant messages for memory extraction");
            return;
        }

        var conversation = sb.ToString();

        var extractionContext = new BasicContext();
        extractionContext.Timeline.InsertSystem(_systemPrompt);
        extractionContext.Timeline.InsertSystem(conversation);

        var agent = new MemorySynthesisAgent("memory_synthesis", _serviceProvider);
        
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
            if (turn == 10)
            {
                _logger.LogError(
                    "Hit {number} turns for memory extraction agent. Dropping memory from {startTime}!",
                    turn, 
                    chat.UtcStarted
                );
                
                return;
            }
            
            var status = await runner.ExecuteTurn();

            if (status == AgentRunner.TurnStatus.CompletedWithError)
            {
                _logger.LogError(
                    "Memory extraction completed with error {error}. Dropping memory from {startTime}!",
                    runner.FinishError,
                    chat.UtcStarted
                );
                
                return;
            }

            if (status == AgentRunner.TurnStatus.CompletedSuccessfully || _memoryConfig.RunForOneTurn)
            {
                _logger.LogInformation(
                    "Memory extraction for {utcStart} - {utcEnd} completed successfully",
                    chat.UtcStarted,
                    chat.UtcFinished
                );
                
                return;
            }
        }
    }
}