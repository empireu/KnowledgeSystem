using System.Text;
using KnowledgeSystem.Agent;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Observer;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Discord.Conversation;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

namespace KnowledgeSystem.Discord;

/// <summary>
///     Observes agent execution and updates a single Discord message in-place.
///     Tool calls/results are shown as blockquote status lines during processing; the final assistant message replaces everything with a rich embed.
/// </summary>
public sealed class DiscordObserver : IAgentObserver
{
    private readonly ILogger<DiscordObserver> _logger;
    private readonly IConversationManager _conversationManager;
    private readonly IDiscordMessageTarget _target;
    private readonly List<ToolStatusEntry> _toolStatus = [];
    private string? _finalResponse;
    private int? _finalConversationTokenCount;
    private bool _hasError;

    public DiscordObserver(ILogger<DiscordObserver> logger, IConversationManager conversationManager, IDiscordMessageTarget target)
    {
        _target = target;
        _logger = logger;
        _conversationManager = conversationManager;
    }

    private string BuildStatusContent()
    {
        if (_toolStatus.Count == 0)
        {
            return "> ▸ *Scheming...*";
        }

        var sb = new StringBuilder();

        foreach (var entry in _toolStatus)
        {
            sb.AppendLine($"> {entry.ToMarkdown()}");
        }

        // Check if any tool is still running (called but no result yet)
        var hasRunning = _toolStatus.Any(e => e.State == ToolState.Running);

        if (hasRunning)
        {
            sb.AppendLine("> ");
            sb.AppendLine("> ◌ *Working...*");
        }
        else
        {
            sb.AppendLine("> ");
            sb.AppendLine("> ▸ *Typing...*");
        }

        return sb.ToString();
    }

    public async Task OnToolCallAsync(AgentRunner runner, ToolCallInfo info, CancellationToken cancellationToken)
    {
        var arguments = string.Join(", ", info.Args.Arguments.Select(kvp => $"`{kvp.Key.ArgumentName}`: {Truncate(kvp.Value ?? "null", 40)}"));
        _toolStatus.Add(new ToolStatusEntry(info.Tool.ToolId, arguments, ToolState.Running));
        await UpdateMessageAsync(cancellationToken);
    }

    public async Task OnToolResultAsync(AgentRunner runner, int indexInCollection, AgentTool tool, ToolExecutionResult result, CancellationToken cancellationToken)
    {
        var entry = _toolStatus.FindLast(e => e.ToolId == tool.ToolId && e.State == ToolState.Running);
      
        if (entry != null)
        {
            entry.State = result.IsSuccessful ? ToolState.Completed : ToolState.Failed;
            entry.Error = result.IsSuccessful ? null : Truncate(result.FormatError(), 80);
        }

        await UpdateMessageAsync(cancellationToken);
    }

    public async Task OnAssistantMessageAsync(AgentRunner runner, string message, CancellationToken cancellationToken)
    {
        _finalResponse = message;

        if (runner is AgentRunner<ConversationalContext> chatRunner)
        {
            var messages = chatRunner.ExecutionContext.ChatMessages;
            _finalConversationTokenCount = _conversationManager.TokenEstimator.CountTokens(messages);
        }
        
        await UpdateMessageAsync(cancellationToken);
    }

    public Task OnAgentCompletedAsync(AgentRunner runner, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task OnErrorAsync(AgentRunner runner, AgentExecutionError error, CancellationToken cancellationToken)
    {
        _hasError = true;

        if (_finalResponse == null)
        {
            // No response yet — show error embed
            var embed = new EmbedProperties()
                .WithTitle("Error")
                .WithDescription(error.Message)
                .WithColor(new Color(0xED4245))
                .WithTimestamp(DateTimeOffset.UtcNow)
                .WithFooter(new EmbedFooterProperties { Text = "MQR Knowledge Agent" });

            try
            {
                await _target.SetEmbedAsync(embed, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update Discord message with error embed");
            }
        }
    }

    private async Task UpdateMessageAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_finalResponse != null)
            {
                // Final response. Use a rich embed:
                var color = _hasError 
                    ? new Color(0xFEE75C)
                    : new Color(0x57F287);

                var embed = new EmbedProperties()
                    .WithDescription(Truncate(_finalResponse, 4096))
                    .WithColor(color)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .WithFooter(new EmbedFooterProperties { Text = "MQR Agent" });

                if (_toolStatus.Count > 0)
                {
                    var toolSummary = string.Join("\n", _toolStatus.Select(e => e.ToCompactMarkdown()));
                   
                    embed = embed.AddFields(new EmbedFieldProperties()
                        .WithName($"Tools Used ({_toolStatus.Count})")
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

                await _target.SetEmbedAsync(embed, cancellationToken);
            }
            else
            {
                // Still processing — update content with status
                await _target.UpdateContentAsync(BuildStatusContent(), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update Discord message");
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

    private enum ToolState { Running, Completed, Failed }

    private sealed class ToolStatusEntry(string toolId, string arguments, ToolState state)
    {
        public string ToolId { get; } = toolId;
        public string Arguments { get; } = arguments;
        public ToolState State { get; set; } = state;
        public string? Error { get; set; }

        public string ToMarkdown() => State switch
        {
            ToolState.Running => $"◌ **`{ToolId}`**({Arguments})",
            ToolState.Completed => $"✓ **`{ToolId}`**({Arguments})",
            ToolState.Failed => $"✗ **`{ToolId}`**({Arguments}) — {Error}",
            _ => $"**`{ToolId}`**({Arguments})"
        };

        public string ToCompactMarkdown() => State switch
        {
            ToolState.Running => $"◌ `{ToolId}`",
            ToolState.Completed => $"✓ `{ToolId}`",
            ToolState.Failed => $"✗ `{ToolId}` — {Error}",
            _ => $"`{ToolId}`"
        };
    }
}
