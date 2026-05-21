using System.Text;
using KnowledgeSystem.Agent;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Observer;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Discord.Conversation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;

namespace KnowledgeSystem.Discord.Observer;

/// <summary>
///     Observes agent execution and updates a single Discord message in-place.
///     Tool calls/results are shown as blockquote status lines during processing; the final assistant message replaces everything with a rich embed.
/// </summary>
public sealed partial class DiscordObserver(
    ILogger<DiscordObserver> logger,
    IConversationManager conversationManager,
    IDiscordMessageTarget target,
    IOptions<ChatOptions> options
) : IAgentObserver
{
    private readonly List<ToolStatusEntry> _toolStatus = [];
    private string? _finalResponse;
    private int? _finalConversationTokenCount;
    private bool _hasError;
    private bool _finalSent;

    private Task? _pendingUpdate;
    private CancellationTokenSource? _debounceCts;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly Lock _stateLock = new();
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(600);

    private ToolStatusEntry[] SnapshotToolStatus()
    {
        lock (_stateLock)
        {
            return _toolStatus.ToArray();
        }
    }

    private static string BuildStatusContent(ToolStatusEntry[] snapshot)
    {
        if (snapshot.Length == 0)
        {
            return "> ▸ *Scheming...*";
        }

        var sb = new StringBuilder();

        foreach (var entry in snapshot)
        {
            sb.AppendLine($"> {entry.ToMarkdown()}");
        }

        // Check if any tool is still running (called but no result yet)
        var hasRunning = snapshot.Any(e => e.State == ToolState.Running);

        sb.AppendLine("> ");

        sb.AppendLine(hasRunning 
            ? "> ◌ *Working...*" 
            : "> ▸ *Typing...*"
        );

        return sb.ToString();
    }

    public async Task OnToolCallAsync(AgentRunner runner, ToolCallInfo info, CancellationToken cancellationToken)
    {
        var args = info.Args.Arguments
        .Select(kvp =>
        {
            var parameterPrintout = Truncate(ChatMessageHelpers.FormatContentValue(kvp.Value) ?? "null", 40);
            
            // Escape newlines:
            parameterPrintout = EscapeNewlines(parameterPrintout);
            
            // Value is wrapped in code because it can contain Markdown that breaks the rendering.
            parameterPrintout = $"`{parameterPrintout}`";
            
            // Format for specific types:
            parameterPrintout = kvp.Key switch
            {
                StringArgument => $"\"{parameterPrintout}\"",
                _ => parameterPrintout
            };
            
            return $"{kvp.Key.ArgumentName}: {parameterPrintout}";
        });
        
        var arguments = string.Join(", ", args);
      
        lock (_stateLock)
        {
            _toolStatus.Add(new ToolStatusEntry(info.Tool.ToolId, arguments, ToolState.Running));
        }
        await UpdateMessageAsync(cancellationToken);
    }

    public async Task OnToolResultAsync(AgentRunner runner, int indexInCollection, AgentTool tool, ToolExecutionResult result, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            var entry = _toolStatus.FindLast(e => e.ToolId == tool.ToolId && e.State == ToolState.Running);
          
            if (entry != null)
            {
                entry.State = result.IsSuccessful ? ToolState.Completed : ToolState.Failed;
                entry.Error = result.IsSuccessful ? null : Truncate(EscapeNewlines(result.FormatError()), 80);
            }
        }

        await UpdateMessageAsync(cancellationToken);
    }
    
    public async Task OnAssistantMessageAsync(AgentRunner runner, string message, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            _finalResponse = message;
        }

        if (runner is AgentRunner<ConversationalContext> chatRunner)
        {
            var messages = chatRunner.ExecutionContext.ChatMessages;
            _finalConversationTokenCount = conversationManager.TokenEstimator.CountTokens(messages);
        }
        
        await UpdateMessageAsync(cancellationToken);
    }

    public async Task OnErrorAsync(AgentRunner runner, AgentExecutionError error, CancellationToken cancellationToken)
    {
        bool isFinal;
        lock (_stateLock)
        {
            _hasError = true;
            isFinal = _finalResponse != null;
        }

        if (!isFinal)
        {
            var embed = new EmbedProperties()
                .WithTitle("Error")
                .WithDescription(error.Message)
                .WithColor(new Color(0xED4245))
                .WithTimestamp(DateTimeOffset.UtcNow)
                .WithFooter(new EmbedFooterProperties { Text = "MQR Knowledge Agent" });

            try
            {
                await target.SetEmbedAsync(embed, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update Discord message with error embed");
            }
        }
    }

    private async Task UpdateMessageAsync(CancellationToken cancellationToken)
    {
        Task? pendingToAwait = null;
        bool isFinal;

        lock (_stateLock)
        {
            isFinal = _finalResponse != null;

            if (isFinal)
            {
                _debounceCts?.Cancel();
                pendingToAwait = _pendingUpdate;
            }
            else if (_pendingUpdate != null)
            {
                return;
            }
            else
            {
                _debounceCts = new CancellationTokenSource();
                _pendingUpdate = DebouncedUpdateAsync(_debounceCts.Token);
            }
        }

        if (isFinal)
        {
            if (pendingToAwait != null)
            {
                try
                {
                    await pendingToAwait;
                }
                catch
                {
                    // ignored
                }
            }

            await _updateLock.WaitAsync(cancellationToken);
            try
            {
                await SendUpdateAsync(cancellationToken);
            }
            finally
            {
                _updateLock.Release();
            }
        }
        else
        {
            await _pendingUpdate!;
        }
    }

    private async Task DebouncedUpdateAsync(CancellationToken debounceToken)
    {
        try
        {
            await Task.Delay(DebounceDelay, debounceToken);
            await _updateLock.WaitAsync(debounceToken);
            try
            {
                await SendUpdateAsync(CancellationToken.None);
            }
            finally
            {
                _updateLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected if superseded by a final-response update
        }
        finally
        {
            lock (_stateLock)
            {
                _pendingUpdate = null;
                _debounceCts = null;
            }
        }
    }

    private async Task SendUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_finalResponse != null)
            {
                lock (_stateLock)
                {
                    if (_finalSent)
                    {
                        return;
                    }
                    
                    _finalSent = true;
                }

                // Final response. Use a rich embed:
                var snapshot = SnapshotToolStatus();
                bool hasError;
                lock (_stateLock)
                {
                    hasError = _hasError;
                }

                var color = hasError
                    ? new Color(0xFEE75C)
                    : new Color(0x57F287);

                var embed = new EmbedProperties()
                    .WithDescription(Truncate(_finalResponse, 4096))
                    .WithColor(color)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .WithFooter(new EmbedFooterProperties { Text = "MQR Agent" });

                if (options.Value.Verbose && snapshot.Length > 0)
                {
                    var toolSummary = string.Join("\n", snapshot.Select(e => e.ToCompactMarkdown()));

                    embed = embed.AddFields(new EmbedFieldProperties()
                        .WithName($"Tools Used ({snapshot.Length})")
                        .WithValue(Truncate(toolSummary, 1024))
                        .WithInline()
                    );
                }

                if (_finalConversationTokenCount.HasValue)
                {
                    embed = embed.AddFields(new EmbedFieldProperties()
                        .WithName("Tokens")
                        .WithValue($"~{_finalConversationTokenCount.Value}")
                        .WithInline()
                    );
                }

                await target.SetEmbedAsync(embed, cancellationToken);
            }
            else
            {
                var snapshot = SnapshotToolStatus();
                await target.UpdateContentAsync(BuildStatusContent(snapshot), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update Discord message");
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - 1)] + "…";
    }
    
    private static string EscapeNewlines(string str)
    {
        str = str.Replace("\r", "\\r");
        str = str.Replace("\n", "\\n");
        return str;
    }
}
