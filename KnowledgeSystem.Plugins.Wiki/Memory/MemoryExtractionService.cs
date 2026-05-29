using System.Text;
using System.Threading.Channels;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Ai;
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

    private readonly MemorySystemConfig _memoryConfig;
    private readonly IChatClient _extractionClient;
    private readonly string _systemPrompt;
    
    private Task? _processingTask;
    private readonly ILogger<MemoryExtractionService> _logger;

    public MemoryExtractionService(ILogger<MemoryExtractionService> logger, IOptions<WikiOptions> options)
    {
        _logger = logger;

        var config = options.Value;
        _memoryConfig = config.Memory ?? throw new InvalidOperationException("Created memory service, but the config is null");
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
                        sb.AppendLine($"({ChatRole.User.Value}) {chatElement.Message.Text}");
                        hasUser = true;
                    }
                    else if (chatElement.Message.Role == ChatRole.Assistant)
                    {
                        sb.AppendLine($"({ChatRole.Assistant.Value}) {chatElement.Message.Text}");
                        hasAssistant = true;
                    }
                    
                    break;
                }
                case ReviewedWikiResponseMarker reviewedResponse:
                {
                    sb.AppendLine($"({ChatRole.Assistant.Value}) {reviewedResponse.VerifiedReport}");
                    hasAssistant = true;
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
        
        Console.WriteLine(conversation);
    }
}