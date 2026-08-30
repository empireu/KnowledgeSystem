using System.Text;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Context.TokenEstimation;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.RunnerEvents;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Api;
using KnowledgeSystem.Events.Api;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

namespace KnowledgeSystem.Plugins.Library;

/// <summary>
///     Integrates a user request with discord. The lifetime of this handler is from the moment the user sends the message, to the moment the final response is generated.
///     <list type="bullet">
///         <item><description>Each round is an LLM turn that may produce an output trace and tool calls.</description></item>
///         <item><description>Tool calls are grouped under their round.</description></item>
///         <item><description>Sub-agents are revealed by peeking at <see cref="AgentToolFrame.RunningSubAgent.Proxy"/>, during <see cref="OnTurnAsync"/>, recursively building the subtree.</description></item>
///         <item><description>The single Discord message is updated in-place via debounced incremental updates, then replaced with a final embed on completion.</description></item>
///         <item><description></description></item>
///     </list>
/// </summary>
public sealed class DiscordMessageIntegration : IEventReceiver
{
    // ReSharper disable UnusedAutoPropertyAccessor.Local

    private sealed class RoundNode
    {
        /// <summary>
        ///     The LLM's output trace. Currently, I couldn't find a way to get it from the API.
        /// </summary>
        public string? OutputTrace { get; init; }

        /// <summary>
        ///     Tool calls made in this round.
        /// </summary>
        public List<ToolCallNode> ToolCalls { get; } = [];
    }

    private sealed class ToolCallNode
    {
        public required string ToolId { get; init; }
        public string Arguments { get; init; } = "";
        public ToolState State { get; set; } = ToolState.Running;
        public string? Error { get; set; }

        /// <summary>
        ///     If this tool is a sub-agent, its internal execution tree.
        /// </summary>
        public ExecutionTree? SubTree { get; set; }
    }

    /// <summary>
    ///     Execution tree for sub-agents.
    /// </summary>
    private sealed class ExecutionTree
    {
        /// <summary>
        ///     Agent name or tool name.
        /// </summary>
        public string Label { get; init; } = "";

        /// <summary>
        ///     Turns within the sub-agent.
        /// </summary>
        public List<RoundNode> Rounds { get; } = [];

        /// <summary>
        ///     True if the sub-agent completed.
        /// </summary>
        public bool IsFinished { get; set; }

        /// <summary>
        ///     If true, then the sub-agent completed with error.
        /// </summary>
        public bool HasError { get; set; }
    }

    // ReSharper restore UnusedAutoPropertyAccessor.Local

    private enum ToolState
    {
        /// <summary>
        ///     The tool is currently executing.
        /// </summary>
        Running,
        /// <summary>
        ///     The tool completed successfully.
        /// </summary>
        Completed,
        /// <summary>
        ///     The tool completed with error.
        /// </summary>
        Failed
    }

    /// <summary>
    ///     If true, a summary of the tool calls will remain with the final output.
    /// </summary>
    public bool Verbose { get; set; } = false;
    
    private readonly ILogger<DiscordMessageIntegration> _logger;
    private readonly AgentRunner<BasicContext> _runner;
    private readonly ITokenEstimator? _tokenEstimator;
    private readonly IDiscordMessageTarget _target;
    
    private readonly List<RoundNode> _rounds = [];
    private RoundNode? _currentRound;
    private string? _finalResponse;
    private List<KeyValuePair<string, string>>? _additionalEmbeds;
    private int? _finalTokens;
    private bool _hasError;
    private bool _finalSent;
    private readonly List<MessageAttachment> _attachments = [];

    private Task? _pendingUpdate;
    private CancellationTokenSource? _debounceCts;
    private string? _thinkingTrace;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly Lock _stateLock = new();
    
    /// <summary>
    ///     Integrates a user request with discord. The lifetime of this handler is from the moment the user sends the message, to the moment the final response is generated.
    ///     <list type="bullet">
    ///         <item><description>Each round is an LLM turn that may produce an output trace and tool calls.</description></item>
    ///         <item><description>Tool calls are grouped under their round.</description></item>
    ///         <item><description>Sub-agents are revealed by peeking at <see cref="AgentToolFrame.RunningSubAgent.Proxy"/>, during <see cref="OnTurnAsync"/>, recursively building the subtree.</description></item>
    ///         <item><description>The single Discord message is updated in-place via debounced incremental updates, then replaced with a final embed on completion.</description></item>
    ///         <item><description></description></item>
    ///     </list>
    /// </summary>
    public DiscordMessageIntegration(ILogger<DiscordMessageIntegration> logger, AgentRunner<BasicContext> runner, ITokenEstimator? tokenEstimator, IDiscordMessageTarget target)
    {
        _logger = logger;
        _runner = runner;
        _tokenEstimator = tokenEstimator;
        _target = target;
    }
    
    public DiscordMessageIntegration(ILogger<DiscordMessageIntegration> logger, AgentRunner<BasicContext> runner, IDiscordMessageTarget target)
    {
        _logger = logger;
        _runner = runner;
        _tokenEstimator = null;
        _target = target;
    }
    
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(600);
    
    [SubscribeEvent]
    public async ValueTask OnTurnAsync(AgentTurnEvent @event, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            switch (@event.TurnStatus)
            {
                case AgentRunner.TurnStatus.ToolCallsReceived:
                    // A new round with tool calls just arrived. _currentRound was set up in OnToolCallsAsync, nothing extra needed.
                    break;
                case AgentRunner.TurnStatus.ToolsStepped:
                    // Tool calls are being executed. Peek at sub-agent trees:
                    UpdateSubAgentTrees();
                    break;
                case AgentRunner.TurnStatus.ToolsFinished:
                    // Tool calls for this round finished. About to go back to LLM:
                    UpdateSubAgentTrees();
                    break;
                case AgentRunner.TurnStatus.CompletionHandled:
                case AgentRunner.TurnStatus.CompletedSuccessfully:
                    // Turn completed without (further) tool calls:
                    break;
                case AgentRunner.TurnStatus.CompletedWithError:
                    _hasError = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(@event), @event.TurnStatus, $"Unhandled agent turn status {@event.TurnStatus}");
            }
        }

        await GetUpdateMessageTask(cancellationToken);
    }

    [SubscribeEvent]
    public async ValueTask OnThinkingAsync(AgentThinkingEvent @event, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            _thinkingTrace = string.IsNullOrWhiteSpace(@event.ThinkingText) ? null : @event.ThinkingText;
        }

        await GetUpdateMessageTask(cancellationToken);
    }

    [SubscribeEvent]
    public async ValueTask OnToolCallsAsync(AgentToolCallsEvent @event, CancellationToken cancellationToken)
    {            
        // Starts a new rounds:

        lock (_stateLock)
        {
            var outputTrace = !string.IsNullOrWhiteSpace(_thinkingTrace)
                ? _thinkingTrace
                : string.IsNullOrWhiteSpace(@event.Response.Text) ? null : @event.Response.Text;

            _thinkingTrace = null;

            var round = new RoundNode
            {
                OutputTrace = outputTrace
            };

            foreach (var info in @event.Calls)
            {
                if (!info.IsValid)
                {
                    continue;
                }

                var args = FormatArguments(info.Args);
                round.ToolCalls.Add(new ToolCallNode
                {
                    ToolId = info.Tool.ToolId,
                    Arguments = args,
                    State = ToolState.Running
                });
            }

            _rounds.Add(round);
            _currentRound = round;
        }

        await GetUpdateMessageTask(cancellationToken);
    }

    [SubscribeEvent]
    public async ValueTask OnToolResultAsync(AgentToolResultEvent @event, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            // Find the most recent round with a Running tool of this ID.
            // We search backwards because nested sub-agent rounds share the same parent round:
            var found = false;
            for (var i = _rounds.Count - 1; i >= 0; i--)
            {
                var round = _rounds[i];
                for (var j = round.ToolCalls.Count - 1; j >= 0; j--)
                {
                    var call = round.ToolCalls[j];
                    if (call.ToolId == @event.Tool.ToolId && call.State == ToolState.Running)
                    {
                        call.State = @event.Result.IsSuccessful ? ToolState.Completed : ToolState.Failed;
                        call.Error = @event.Result.IsSuccessful ? null : Truncate(EscapeNewlines(@event.Result.FormatError()), 80);
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    break;
                }
            }
        }

        await GetUpdateMessageTask(cancellationToken);
    }

    /// <summary>
    ///     Handles a direct response, which is considered not verified.
    /// </summary>
    [SubscribeEvent]
    public async ValueTask OnAssistantMessageAsync(AgentMessageEvent @event, CancellationToken cancellationToken)
    {
        await PresentMessage(@event.Response.Text, null, cancellationToken);
    }
    
    [SubscribeEvent]
    public ValueTask OnAddAttachmentAsync(AddAttachmentEvent @event, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            _attachments.Add(new MessageAttachment(@event.Name, Encoding.UTF8.GetBytes(@event.Content)));
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Call this to finalize the interaction and present the final message.
    /// </summary>
    /// <param name="content"></param>
    /// <param name="additionalEmbeds"></param>
    /// <param name="cancellationToken"></param>
    public async Task PresentMessage(string content, List<KeyValuePair<string, string>>? additionalEmbeds, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            _thinkingTrace = null;
            _finalResponse = content;
        }

        var messages = _runner.ExecutionContext.ChatMessages;

        _additionalEmbeds = additionalEmbeds;
        _finalTokens = _tokenEstimator?.CountTokens(messages);
        
        await GetUpdateMessageTask(cancellationToken);
    }
    
    [SubscribeEvent]
    public async ValueTask OnErrorAsync(AgentErrorEvent @event, CancellationToken cancellationToken)
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
                .WithDescription(@event.Error.Message)
                .WithColor(new Color(0xED4245))
                .WithTimestamp(DateTimeOffset.UtcNow)
                .WithFooter(new EmbedFooterProperties { Text = "MQR Knowledge Agent" });

            try
            {
                await _target.SetEmbedAsync(embed, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update Discord message with error embed");
            }
        }
    }

    [SubscribeEvent]
    public async ValueTask OnAgentCompletedAsync(AgentCompletedEvent @event, CancellationToken cancellationToken)
    {
        // Final update is triggered by OnAssistantMessageAsync, SendUpdateAsync.
        // Just ensure we flush any pending update:
        await GetUpdateMessageTask(cancellationToken);
    }
    
    /// <summary>
    ///     Walks the runner's <see cref="AgentRunner.ActiveToolCalls"/> looking for <see cref="AgentToolFrame.RunningSubAgent"/> frames and peeks at their internal runner state to build a subtree.
    /// </summary>
    private void UpdateSubAgentTrees()
    {
        foreach (var frame in _runner.ActiveToolCalls)
        {
            if (frame is not AgentToolFrame.RunningSubAgent subFrame)
            {
                continue;
            }

            var subRunner = subFrame.Proxy?.AgentRunner;
            if (subRunner == null)
            {
                continue;
            }

            // Find the matching ToolCallNode in our current round:
            var callNode = _currentRound?.ToolCalls.FirstOrDefault(t => t.ToolId == subFrame.Tool.ToolId && t.SubTree == null);

            if (callNode == null)
            {
                continue;
            }

            var tree = new ExecutionTree
            {
                Label = subFrame.Tool.ToolId,
                IsFinished = subRunner.IsFinished,
                HasError = subRunner.FinishError != null
            };

            // Peek at sub-agent's execution context chat history to reconstruct rounds.
            ExtractSubAgentRounds(subRunner, tree);

            callNode.SubTree = tree;
        }
    }

    /// <summary>
    ///     Recursively extracts rounds from a sub-agent runner by inspecting its execution context and active tool calls.
    /// </summary>
    private static void ExtractSubAgentRounds(AgentRunner parentRunner, ExecutionTree tree)
    {
        // If the sub-agent has active tool calls, represent them as a synthetic round:
        var activeCalls = parentRunner.ActiveToolCalls;
        if (activeCalls.Count <= 0)
        {
            return;
        }

        var round = new RoundNode();
        foreach (var frame in activeCalls)
        {
            switch (frame)
            {
                case AgentToolFrame.Hallucination h:
                    round.ToolCalls.Add(new ToolCallNode
                    {
                        ToolId = $"?{h.FunctionName}",
                        Arguments = "(hallucinated)",
                        State = ToolState.Failed,
                        Error = Truncate(EscapeNewlines(h.ErrorMessage), 80)
                    });
                    break;

                case AgentToolFrame.MissingArgs m:
                    round.ToolCalls.Add(new ToolCallNode
                    {
                        ToolId = "(missing args)",
                        Arguments = "",
                        State = ToolState.Failed,
                        Error = Truncate(EscapeNewlines(m.ErrorMessage), 80)
                    });
                    break;

                case AgentToolFrame.PlainRunningFrame p:
                    round.ToolCalls.Add(new ToolCallNode
                    {
                        ToolId = p.Tool.ToolId,
                        Arguments = FormatArguments(p.Args),
                        State = p.Result != null
                            ? p.Result.IsSuccessful ? ToolState.Completed : ToolState.Failed
                            : ToolState.Running,
                      
                        Error = p.Result is { IsSuccessful: false }
                            ? Truncate(EscapeNewlines(p.Result.FormatError()), 80)
                            : null
                    });
                    break;

                case AgentToolFrame.RunningSubAgent s:
                {
                    var subTreeNode = new ToolCallNode
                    {
                        ToolId = s.Tool.ToolId,
                        Arguments = FormatArguments(s.Args),
                        State = s.Result != null
                            ? s.Result.IsSuccessful ? ToolState.Completed : ToolState.Failed
                            : ToolState.Running,
                        
                        Error = s.Result is { IsSuccessful: false }
                            ? Truncate(EscapeNewlines(s.Result.FormatError()), 80)
                            : null
                    };

                    // Recursively peek deeper:
                    if (s.Proxy?.AgentRunner is { } subRunner)
                    {
                        var subTree = new ExecutionTree
                        {
                            Label = s.Tool.ToolId,
                            IsFinished = subRunner.IsFinished,
                            HasError = subRunner.FinishError != null
                        };
                        
                        ExtractSubAgentRounds(subRunner, subTree);
                        subTreeNode.SubTree = subTree;
                    }

                    round.ToolCalls.Add(subTreeNode);
                    break;
                }
            }
        }

        tree.Rounds.Add(round);
    }

    /// <summary>
    ///     Builds the status text that replaces the message content during execution.
    ///     Shows rounds, output traces, and the tool call tree (including sub-agents).
    /// </summary>
    private string BuildStatusContent()
    {
        const int maxLines = 35;
        var lines = new List<string>();

        string? thinking;
        lock (_stateLock)
        {
            thinking = _thinkingTrace;
        }

        if (!string.IsNullOrWhiteSpace(thinking))
        {
            lines.Add("> ── Thinking ──");
            lines.Add($"> {TruncateTail(EscapeNewlines(thinking), 400).Replace("`", "'")}");
            lines.Add("> ");
        }

        foreach (var round in _rounds)
        {
            lines.Add($"> ── Round {_rounds.IndexOf(round) + 1} ──");

            if (!string.IsNullOrWhiteSpace(round.OutputTrace))
            {
                var trace = Truncate(EscapeNewlines(round.OutputTrace), 200).Replace("`", "'");
                lines.Add($"> “{trace}”");
            }

            foreach (var call in round.ToolCalls)
            {
                AppendToolCallNode(lines, call, 0);
            }

            lines.Add("> ");
        }

        var hasRunning = _rounds.Any(r => r.ToolCalls.Any(t => t.State == ToolState.Running));
        var statusLine = hasRunning
            ? "> · Working..."
            : "> ▸ Processing...";

        var result = new StringBuilder();
        var startIndex = Math.Max(0, lines.Count - maxLines);

        if (startIndex > 0)
        {
            result.AppendLine("> ... *(truncated)*");
        }

        for (var i = startIndex; i < lines.Count; i++)
        {
            result.AppendLine(lines[i]);
        }

        result.AppendLine(statusLine);

        return result.ToString();
    }

    private static void AppendToolCallNode(List<string> lines, ToolCallNode call, int depth)
    {
        var indent = depth > 0 ? new string(' ', depth * 2) + "└ " : "";
        var prefix = call.State switch
        {
            ToolState.Running => "·",
            ToolState.Completed => " ",
            ToolState.Failed => "!",
            _ => "·"
        };

        var args = string.IsNullOrEmpty(call.Arguments) ? "" : $" {call.Arguments}";
        var error = call.Error != null ? $" — {call.Error}" : "";

        lines.Add($"> {indent}{prefix} **{call.ToolId}**{args}{error}");
        
        if (call.SubTree != null)
        {
            var subTree = call.SubTree;
            foreach (var subRound in subTree.Rounds)
            {
                foreach (var subCall in subRound.ToolCalls)
                {
                    AppendToolCallNode(lines, subCall, depth + 1);
                }
            }
        }
    }

    /// <summary>
    ///     Renders the final embed after execution completes.
    /// </summary>
    private EmbedProperties BuildFinalEmbed()
    {
        List<RoundNode> snapshot;
        lock (_stateLock)
        {
            snapshot = [.._rounds];
        }

        var color = _hasError
            ? new Color(0xFEE75C)
            : new Color(0x57F287);

        var embed = new EmbedProperties()
            .WithDescription(Truncate(_finalResponse ?? "", 4096))
            .WithColor(color)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter(new EmbedFooterProperties { Text = "MQR Agent" });

        if (Verbose && snapshot.Count > 0)
        {
            var toolSummary = new StringBuilder();
            var totalTools = 0;
            foreach (var round in snapshot)
            {
                foreach (var call in round.ToolCalls)
                {
                    if (toolSummary.Length >= 900)
                    {
                        toolSummary.Append("...");
                        break;
                    }

                    var icon = call.State switch
                    {
                        ToolState.Completed => "+",
                        ToolState.Failed => "!",
                        _ => "·"
                    };
                    
                    toolSummary.AppendLine($"{icon} {call.ToolId}");
                    totalTools++;
                }
            }

            embed = embed.AddFields(new EmbedFieldProperties()
                .WithName($"Tools Used ({totalTools})")
                .WithValue(Truncate(toolSummary.ToString(), 1024))
                .WithInline()
            );
        }

        embed = embed.AddFields(new EmbedFieldProperties()
            .WithName("Tokens")
            .WithValue(_finalTokens.ToString())
            .WithInline()
        );

        if (_additionalEmbeds != null)
        {
            foreach (var (name, value) in _additionalEmbeds)
            {
                embed = embed.AddFields(new EmbedFieldProperties()
                    .WithName(name)
                    .WithValue(value)
                    .WithInline()
                );       
            }
        }

        return embed;
    }
    
    private async Task GetUpdateMessageTask(CancellationToken cancellationToken)
    {
        Task? pendingToAwait;

        lock (_stateLock)
        {
            var isFinal = _finalResponse != null;

            if (isFinal)
            {
                _debounceCts?.Cancel();
                pendingToAwait = _pendingUpdate;
            }
            else
            {
                if (_pendingUpdate == null)
                {
                    _debounceCts = new CancellationTokenSource();
                    _pendingUpdate = DebouncedUpdateAsync(_debounceCts.Token);
                }

                return;
            }
        }

        // Final path: await the pending debounce (if any), then flush the final embed.
        if (pendingToAwait != null)
        {
            try
            {
                await pendingToAwait;
            }
            catch(Exception ex) when(ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Final observer update await produced error");
            }
        }

        await FlushFinalAsync(cancellationToken);
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

    private async Task FlushFinalAsync(CancellationToken cancellationToken)
    {
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

                var embed = BuildFinalEmbed();
            
                IReadOnlyList<MessageAttachment>? attachments;
                lock (_stateLock)
                {
                    attachments = _attachments.ToArray();
                    _attachments.Clear();
                }
            
                await _target.SetEmbedAsync(embed, attachments, cancellationToken);
            }
            else
            {
                var content = BuildStatusContent();
                await _target.UpdateContentAsync(content, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Discord message");
        }
    }
    
    private static string FormatArguments(ArgumentExtractionResult args)
    {
        var parts = args.Arguments.Select(kvp =>
        {
            var value = Truncate(ChatMessageHelpers.FormatContentValue(kvp.Value) ?? "null", 40);
            value = value.Replace("`", "'");
            value = EscapeNewlines(value);

            if (kvp.Key is StringArgument)
            {
                value = $"\"{value}\"";
            }

            return $"{kvp.Key.ArgumentName}: `{value}`";
        }).ToList();

        if (parts.Count == 0)
        {
            return "";
        }

        return $"({string.Join(", ", parts)})";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }
        
        return value[..(maxLength - 3)] + "...";
    }

    private static string TruncateTail(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return "..." + value[^(maxLength - 3)..];
    }

    private static string EscapeNewlines(string str)
    {
        return str.Replace("\r", "\\r").Replace("\n", "\\n");
    }
}
