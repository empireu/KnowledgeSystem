using System.ClientModel;
using KnowledgeSystem.Agents.Orchestration.Observer;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using OpenAI.Chat;
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Agents.Orchestration;

public class AgentRunner<TContext, TResult>(
    Agent<TContext, TResult> agent,
    TContext context,
    IAgentObserver observer,
    ChatClient client)
    where TContext : AgentExecutionContext
    where TResult : class
{
    public Agent<TContext, TResult> Agent { get; } = agent;
    public TContext ExecutionContext { get; } = context;
    public IAgentObserver Observer { get; } = observer;
    public ChatClient Client { get; } = client;
    public bool Completed { get; private set; }
    
    private bool _completedWithError;

    /// <summary>
    ///     Executes a single turn. Does one LLM call, then handles tool calls or completion.
    ///     Returns true if the agent is still running, and false if completed.
    /// </summary>
    public async Task<bool> ExecuteTurn(CancellationToken cancellationToken = default)
    {
        var chatOptions = new ChatCompletionOptions();
       
        Agent.ToolRegistry.ToolSet.AddToOptions(chatOptions);
       
        ClientResult<ChatCompletion> result;
        try
        {
            result = await Client.CompleteChatAsync(
                ExecutionContext.ChatMessages,
                chatOptions, 
                cancellationToken
             );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Observer.OnErrorAsync(new AgentExecutionError($"LLM request failed: {ex.Message}", true), CancellationToken.None);
            Completed = true;
            _completedWithError = true;
            await Observer.OnAgentCompletedAsync(new AgentExecutionResult(AgentExecutionResult.Status.Error), CancellationToken.None);
            return false;
        }

        var completion = result.Value;

        // Tool calls required:
        if (completion.FinishReason == ChatFinishReason.ToolCalls)
        {
            ExecutionContext.InsertAssistantCompletion(completion);
            await ExecuteToolCallsAsync(completion.ToolCalls, cancellationToken);
            return true;
        }

        // Non-tool completion. Continue with result and notify:
        var textContent = string.Join("\n", completion.Content
            .Where(p => p.Kind == ChatMessageContentPartKind.Text)
            .Select(p => p.Text)); // Never seen multiple contents, but this is a fallback anyway.

        if (!string.IsNullOrEmpty(textContent))
        {
            await Observer.OnAssistantMessageAsync(textContent, cancellationToken);
        }

        // Let the agent decide whether this finishes execution:
        var agentResult = await Agent.CompleteAsync(completion, ExecutionContext);

        if (agentResult.FinishesAgent)
        {
            Completed = true;
            await Observer.OnAgentCompletedAsync(new AgentExecutionResult(AgentExecutionResult.Status.FinishedSuccessfully), cancellationToken);
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Runs the agent to completion, looping turns until done or canceled.
    /// </summary>
    public async Task<AgentExecutionResult> RunAsync(CancellationToken cancellationToken = default)
    {
        while (!Completed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool stillRunning;
            try
            {
                stillRunning = await ExecuteTurn(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Completed = true;
                await Observer.OnAgentCompletedAsync(new AgentExecutionResult(AgentExecutionResult.Status.Canceled), CancellationToken.None);
                return new AgentExecutionResult(AgentExecutionResult.Status.Canceled);
            }

            if (!stillRunning)
            {
                break;
            }
        }

        return _completedWithError
            ? new AgentExecutionResult(AgentExecutionResult.Status.Error)
            : new AgentExecutionResult(AgentExecutionResult.Status.FinishedSuccessfully);
    }
    
    private async Task ExecuteToolCallsAsync(IReadOnlyList<ChatToolCall> toolCalls, CancellationToken ct)
    {
        var callInfos = new List<ToolCallInfo>();
        var matchedCalls = new List<(ChatToolCall Call, AgentTool Tool, ArgumentExtractionResult Args, int OriginalIndex)>();

        for (var i = 0; i < toolCalls.Count; i++)
        {
            var toolCall = toolCalls[i];

            if (!Agent.ToolRegistry.ToolSet.TryMatchTool(toolCall, out var tool))
            {
                var hallucinatedMessage = Agent.OnHallucinatedTool(toolCall.FunctionName) ?? $"Invalid tool {toolCall.FunctionName}!";
                ExecutionContext.InsertToolResult(toolCall.Id, hallucinatedMessage);
                await Observer.OnErrorAsync(new AgentExecutionError($"Invalid tool: {toolCall.FunctionName}", false), ct);
                continue;
            }

            var args = ArgumentExtractionResult.ExtractArguments(tool, toolCall.FunctionArguments);

            if (args.Status == ArgumentExtractionResult.ExtractionStatus.IncompleteArguments)
            {
                var handler = Agent.ToolRegistry.Handlers[tool];
                var missing = string.Join(", ", args.MissingArguments.Select(a => a.ArgumentName));
                var missingMessage = handler.OnMissingArguments(args) ?? $"Error: missing required arguments: {missing}";
                ExecutionContext.InsertToolResult(toolCall.Id, missingMessage);
                await Observer.OnErrorAsync(new AgentToolExecutionError($"Missing args for {tool.ToolId}: {missing}", false, tool, i), ct);
                continue;
            }

            callInfos.Add(new ToolCallInfo
            {
                Tool = tool,
                Args = args
            });
            matchedCalls.Add((toolCall, tool, args, i));
        }

        if (callInfos.Count > 0)
        {
            await Observer.OnToolCallAsync(callInfos.ToArray(), ct);
        }

        for (var i = 0; i < matchedCalls.Count; i++)
        {
            var (call, tool, args, originalIndex) = matchedCalls[i];
            var handler = Agent.ToolRegistry.Handlers[tool];

            ToolExecutionResult execResult;
            try
            {
                execResult = await handler.ExecuteAsync(args, ExecutionContext, ct);
            }
            catch (Exception ex)
            {
                execResult = new ToolExecutionResult(tool, ToolExecutionResult.Status.Error, null, ex.Message, ex);
            }

            var output = execResult.ExecutionStatus == ToolExecutionResult.Status.Success
                ? execResult.Result
                : FormatToolExecutionError(execResult);

            ExecutionContext.InsertToolResult(call.Id, output);
            await Observer.OnToolResultAsync(new ToolCallResult { IndexInCollection = originalIndex, Tool = tool, Content = output }, ct);
        }
    }

    private static string FormatToolExecutionError(ToolExecutionResult result)
    {
        if (result.ErrorMessage == null && result.ThrownException == null)
        {
            return "Unspecified error";
        }

        if (result.ErrorMessage != null && result.ThrownException != null)
        {
            return $"{result.ErrorMessage}. Exception: {result.ThrownException.Message}";
        }

        if (result.ErrorMessage != null)
        {
            return result.ErrorMessage;
        }

        if (!string.IsNullOrEmpty(result.ThrownException!.Message))
        {
            return result.ThrownException.Message;
        }

        return result.ThrownException.ToString();
    }
}