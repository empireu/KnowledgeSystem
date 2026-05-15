using System.ClientModel;
using System.Diagnostics;
using KnowledgeSystem.Agents.Orchestration.Observer;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using OpenAI.Chat;
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Agents.Orchestration;

/// <summary>
///     Represents a frame in the call stack. The tool could be a simple tool or an agent.
/// </summary>
public abstract class AgentToolFrame
{
    /// <summary>
    ///     Represents a tool call. The tool could be a simple tool or an agent.
    ///     It tool ID could also be hallucinated. It will be kept in the frame stack so it's resolved in the correct order.
    /// </summary>
    /// <param name="toolId">The exact ID passed by the LLM, which could be hallucinated. This can be verified by the concrete type.</param>
    private AgentToolFrame(string toolId)
    {
        ToolId = toolId;
    }

    public string ToolId { get; }

    /// <summary>
    ///     Represents a hallucinated tool.
    /// </summary>
    public sealed class Hallucination(string toolId, string errorMessage) : AgentToolFrame(toolId)
    {
        /// <summary>
        ///     The message that gets reported to the LLM.
        /// </summary>
        public string ErrorMessage { get; } = errorMessage;
    }

    /// <summary>
    ///     Represents a tool call that is missing some required arguments.
    /// </summary>
    /// <param name="toolId"></param>
    /// <param name="errorMessage"></param>
    public sealed class MissingArgs(string toolId, string errorMessage) : AgentToolFrame(toolId)
    {
        /// <summary>
        ///     The message that gets reported to the LLM.
        /// </summary>
        public string ErrorMessage { get; } = errorMessage;
    }

    /// <summary>
    ///     Interface for a resolved tool call.
    /// </summary>
    public abstract class RunningFrame : AgentToolFrame
    {
        /// <summary>
        ///     Interface for a resolved tool call.
        /// </summary>
        internal RunningFrame(AgentTool tool, ArgumentExtractionResult args) : base(tool.ToolId)
        {
            Tool = tool;
            Args = args;
        }

        /// <summary>
        ///     The tool being called.
        /// </summary>
        public AgentTool Tool { get; }

        /// <summary>
        ///     The arguments for the call.
        /// </summary>
        public ArgumentExtractionResult Args { get; }
        
        /// <summary>
        ///     The final result. Always non-null when the tool finished.
        /// </summary>
        public ToolExecutionResult? Result { get; internal set; }
    }
    
    /// <summary>
    ///     Represents a tool call that resolved to a registered tool successfully. This doesn't invoke any sub-agents and is simply a routine that will execute.
    /// </summary>
    public sealed class Plain(AgentTool tool, ArgumentExtractionResult args) : RunningFrame(tool, args)
    {
        /// <summary>
        ///     The running task. Started and set on the next turn, after the frame is pushed.
        /// </summary>
        public Task<ToolExecutionResult>? StartedTask { get; internal set; }
    }
    
    /// <summary>
    ///     Tool call that runs a sub-agent.
    /// </summary>
    /// <param name="tool"></param>
    /// <param name="args"></param>
    public sealed class SubAgent(AgentTool tool, ArgumentExtractionResult args) : RunningFrame(tool, args)
    {
        /// <summary>
        ///     The proxy holding the runner and the stepping routine. Set on the next turn.
        /// </summary>
        public ISubAgentProxy? Proxy { get; internal set; }
    }
}

public abstract class AgentRunner
{
    /// <summary>
    ///     The observer for the whole execution tree, inherited from the parent.
    /// </summary>
    public IAgentObserver Observer { get; }
    
    /// <summary>
    ///     The chat client (either inherited from the parent, or a fresh one).
    /// </summary>
    public ChatClient Client { get; }
    
    /// <summary>
    ///     The parent execution context. Null if this is the root of the execution tree.
    /// </summary>
    public AgentRunner? Parent { get; }
    
    /// <summary>
    ///     The agent this runner deals with.
    /// </summary>
    public abstract Agent Agent { get; }

    /// <summary>
    ///     The agent runner at the top of the chain.
    ///     Will return this runner if <see cref="Parent"/> is null.
    /// </summary>
    public AgentRunner TopMostRunner { get; }

    protected readonly List<AgentToolFrame> ToolCallsInternal = [];
    
    /// <summary>
    ///     Gets the ongoing tool calls.
    /// </summary>
    public IReadOnlyList<AgentToolFrame> ActiveToolCalls => ToolCallsInternal;

    protected readonly ICompletionFactory CompletionFactory;
    
    protected AgentRunner(IAgentObserver observer, ChatClient client, AgentRunner? parent, ICompletionFactory? completionFactory)
    {
        Observer = observer;
        Client = client;
        Parent = parent;
        
        var top = this;
        while (top.Parent != null)
        {
            top = top.Parent;
        }

        TopMostRunner = top;
        CompletionFactory = completionFactory ?? DefaultCompletionFactory.Instance;
    }
    
    /// <summary>
    ///     If true, the execution finished. Can be either successful, or errored. To see which, check <see cref="FinishError"/>, which is always non-null when the turn completed with error.
    /// </summary>
    public bool IsFinished { get; protected set; }
    
    /// <summary>
    ///     The error. Set when a turn completes with a critical error.
    /// </summary>
    public AgentExecutionError? FinishError { get; protected set; }
    
    public enum TurnStatus
    {
        /// <summary>
        ///     Indicates the agent started running tools.
        /// </summary>
        ToolCallsReceived,
        /// <summary>
        ///     The current tool calls have been stepped.
        /// </summary>
        ToolsStepped,
        /// <summary>
        ///     The current tool calls have finished.
        /// </summary>
        ToolsFinished,
        /// <summary>
        ///     Indicates the execution completed successfully, and the agent can be discarded.
        /// </summary>
        CompletedSuccessfully,
        /// <summary>
        ///     Indicates the execution completed with error.
        /// </summary>
        CompletedWithError,
        /// <summary>
        ///     Indicates that a non-tool-call completion was handled by the agent.
        /// </summary>
        CompletionHandled
    }
    
    public interface ICompletionFactory
    {
        /// <summary>
        ///     Creates the chat completion request for a turn.
        /// </summary>
        ChatCompletionOptions CreateOptionsForTurn(AgentRunner runner);
    }

    public sealed class DefaultCompletionFactory : ICompletionFactory
    {
        public static readonly DefaultCompletionFactory Instance = new();
        
        private DefaultCompletionFactory() { }
        
        public ChatCompletionOptions CreateOptionsForTurn(AgentRunner runner)
        {
            return new ChatCompletionOptions();
        }
    }
}

public sealed class AgentRunner<TContext>(
    IAgentObserver observer,
    ChatClient client,
    Agent<TContext> agent,
    AgentRunner? parent,
    TContext context,
    CancellationToken cancellationToken,
    AgentRunner.ICompletionFactory? completionFactory = null
) : AgentRunner(observer, client, parent, completionFactory)
    where TContext : AgentExecutionContext
{
    public override Agent<TContext> Agent { get; } = agent;

    public TContext ExecutionContext { get; } = context;
    
    public async Task<TurnStatus> ExecuteTurn()
    {
        if (ToolCallsInternal.Count > 0)
        {
            if (await StepTools())
            {
                return TurnStatus.ToolsStepped;
            }
            
            return TurnStatus.ToolsFinished;
        }
        
        var chatOptions = CompletionFactory.CreateOptionsForTurn(this);
        Agent.ToolRegistry.ToolSet.AddToOptions(chatOptions);
        
        // Executes the LLM call and raises the error and completion events:
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
            FinishError = new AgentExecutionError($"LLM request failed: {ex.Message}", true);
            IsFinished = true;
            
            await Observer.OnErrorAsync(this, FinishError, cancellationToken);
            await Observer.OnAgentCompletedAsync(this, cancellationToken);
            
            return TurnStatus.CompletedWithError;
        }
        
        var completion = result.Value;
        
        // Tool calls required:
        if (completion.FinishReason == ChatFinishReason.ToolCalls)
        {
            ExecutionContext.InsertAssistantCompletion(completion);
            await BeginToolCalls(completion.ToolCalls);
            return TurnStatus.ToolCallsReceived;
        }
        
        // Non-tool completion. Continue with result and notify:
        var textContent = string.Join("\n", completion.Content
            .Where(p => p.Kind == ChatMessageContentPartKind.Text)
            .Select(p => p.Text)); // Never seen multiple contents, but this is a fallback anyway.

        if (!string.IsNullOrEmpty(textContent))
        {
            await Observer.OnAssistantMessageAsync(this, textContent, cancellationToken);
        }

        // Let the agent decide whether this finishes execution:
        var completionResult = await Agent.HandleCompletion(completion, ExecutionContext);

        if (!completionResult.CompletesExecution)
        {
            return TurnStatus.CompletionHandled;
        }

        IsFinished = true;
        FinishError = completionResult.Error;
        
        await Observer.OnAgentCompletedAsync(this, cancellationToken);

        return FinishError == null 
            ? TurnStatus.CompletedSuccessfully 
            : TurnStatus.CompletedWithError;
    }

    /// <summary>
    ///     Steps the current tools.
    /// </summary>
    /// <returns>True if more steps are needed. Otherwise, false.</returns>
    private async Task<bool> StepTools()
    {
        AgentToolFrame.RunningFrame? pendingFrame = null;
        var indexInCollection = -1;
        
        for (var frameIndex = 0; frameIndex < ToolCallsInternal.Count; frameIndex++)
        {
            var agentToolFrame = ToolCallsInternal[frameIndex];

            if (agentToolFrame is AgentToolFrame.RunningFrame { Result: null } runningFrame)
            {
                pendingFrame = runningFrame;
                indexInCollection = frameIndex;
                break;
            }
        }
        
        // Frames still need execution:
        if (pendingFrame != null)
        {
            switch (pendingFrame)
            {
                case AgentToolFrame.Plain plainFrame:
                {
                    if (plainFrame.StartedTask == null)
                    {
                        var handler = (ToolHandler<TContext>.Plain) Agent.ToolRegistry.Handlers[plainFrame.Tool];

                        plainFrame.StartedTask = handler.ExecuteAsync(
                            this,
                            plainFrame.Args,
                            ExecutionContext,
                            cancellationToken
                        );

                        return true;
                    }
                    
                    var result = await plainFrame.StartedTask;
                    
                    await Observer.OnToolResultAsync(
                        this,
                        indexInCollection,
                        plainFrame.Tool,
                        result,
                        cancellationToken
                    );

                    plainFrame.Result = result;
                    
                    return true;
                }
                case AgentToolFrame.SubAgent subAgentFrame:
                {
                    if (subAgentFrame.Proxy == null)
                    {
                        var handler = (ToolHandler<TContext>.SubAgent) Agent.ToolRegistry.Handlers[subAgentFrame.Tool];

                        subAgentFrame.Proxy = await handler.BeginSubAgentExecution(
                            this, 
                            subAgentFrame.Args,
                            ExecutionContext,
                            cancellationToken
                        );

                        return true;
                    }

                    var result = await subAgentFrame.Proxy.StepAsync();

                    if (result != null)
                    {
                        await Observer.OnToolResultAsync(
                            this,
                            indexInCollection,
                            subAgentFrame.Tool,
                            result,
                            cancellationToken
                        );
                        
                        subAgentFrame.Result = result;
                    }
                    
                    return true;
                }
                default:
                    throw new Exception($"Invalid tool frame {pendingFrame}");
            }
        }
        
        // Insert results:
        for (var index = 0; index < ToolCallsInternal.Count; index++)
        {
            var toolCall = ToolCallsInternal[index];
            
            ToolExecutionResult result;
            switch (toolCall)
            {
                case AgentToolFrame.Hallucination hallucination:
                    ExecutionContext.InsertToolResult(hallucination.ToolId, hallucination.ErrorMessage);
                    continue;
                case AgentToolFrame.MissingArgs missingArgs:
                    ExecutionContext.InsertToolResult(missingArgs.ToolId,  missingArgs.ErrorMessage);
                    continue;
                case AgentToolFrame.RunningFrame running:
                    result = running.Result;
                    break;
                default:
                    throw new Exception($"Invalid tool frame {pendingFrame}");
            }
            
            Debug.Assert(result != null);

            var output = result.IsSuccessful
                ? result.Output
                : result.FormatError();

            ExecutionContext.InsertToolResult(result.Tool.ToolId, output);
        }
        
        ToolCallsInternal.Clear();

        return false;
    }
    
    /// <summary>
    ///     Represents a tool that was resolved.
    /// </summary>
    private readonly struct ToolCall
    {
        /// <summary>
        ///     The original call from the API.
        /// </summary>
        public required ChatToolCall Call { get; init; }
        
        public string ToolId { get; init; }
        
        /// <summary>
        ///     The resolved tool, if the tool ID wasn't hallucinated.
        /// </summary>
        public required AgentTool? Tool { get; init; }
        
        /// <summary>
        ///     The original index in the (parallel) tool calls.
        /// </summary>
        public required int OriginalIndex { get; init; }
        
        /// <summary>
        ///     The extracted arguments, for non-hallucinated tools.
        /// </summary>
        public required ArgumentExtractionResult? Args { get; init; }
    }
    
    private async Task BeginToolCalls(IReadOnlyList<ChatToolCall> toolCalls)
    {
        // Will hold resolved and hallucinated tools, in the error they arrived:
        var calls = new List<ToolCall>();

        // Extracts the valid tool calls and hallucinated tool calls:
        for (var callIndex = 0; callIndex < toolCalls.Count; callIndex++)
        {
            var toolCall = toolCalls[callIndex];

            if (Agent.ToolRegistry.ToolSet.TryMatchTool(toolCall, out var tool))
            {
                calls.Add(new ToolCall
                {
                    Call = toolCall,
                    ToolId = toolCall.Id,
                    Tool = tool,
                    OriginalIndex = callIndex,
                    Args = ArgumentExtractionResult.ExtractArguments(tool, toolCall.FunctionArguments)
                });
            }
            else
            {
                calls.Add(new ToolCall
                {
                    Call = toolCall,
                    ToolId = toolCall.Id,
                    Tool = null,
                    OriginalIndex = callIndex,
                    Args = null
                });
            }
        }

        // Dispatches the events in order:
        for (var callIndex = 0; callIndex < calls.Count; callIndex++)
        {
            var toolCall = calls[callIndex];

            if (toolCall.Tool == null)
            {
                await Observer.OnErrorAsync(
                    this,
                    new AgentToolHallucinationError(
                        $"Invalid tool \"{toolCall.ToolId}\"", 
                        false,
                        toolCall.ToolId, 
                        callIndex
                    ),
                    cancellationToken
                );
            }
            else
            {
                var args = toolCall.Args!;
                
                if (args.Status == ArgumentExtractionResult.ExtractionStatus.Success)
                {
                    await Observer.OnToolCallAsync(
                        this,
                        new ToolCallInfo
                        {
                            Tool = toolCall.Tool,
                            Args = args
                        },
                        cancellationToken
                    );
                }
                else
                {
                    await Observer.OnErrorAsync(
                        this,
                        new AgentToolIncompleteArgumentsError(
                            $"Missing arguments for \"{toolCall.ToolId}\"", 
                            false,
                            toolCall.ToolId, 
                            callIndex,
                            args
                        ),
                        cancellationToken
                    );
                }
            }
        }

        // Pushes each call to the pending list.
        // Does not start their execution yet, but it does resolve the immediate errors.
        for (var callIndex = 0; callIndex < calls.Count; callIndex++)
        {
            var toolCall = calls[callIndex];

            if (toolCall.Tool == null)
            {
                var message = Agent.GetToolHallucinationError(toolCall.ToolId) ??
                              GetDefaultToolHallucinationResult(toolCall.ToolId);
              
                ToolCallsInternal.Add(new AgentToolFrame.Hallucination(toolCall.ToolId, message));
            }
            else
            {
                var tool = toolCall.Tool!;
                var args = toolCall.Args!;
                var handler = Agent.ToolRegistry.Handlers[tool];

                if (args.Status == ArgumentExtractionResult.ExtractionStatus.Success)
                {
                    switch (handler)
                    {
                        case ToolHandler<TContext>.Plain:
                            ToolCallsInternal.Add(new AgentToolFrame.Plain(tool, args));
                            break;
                        case ToolHandler<TContext>.SubAgent:
                            ToolCallsInternal.Add(new AgentToolFrame.SubAgent(tool, args));
                            break;
                        default:
                            throw new Exception($"Invalid tool handler {handler}");
                    }
                }
                else
                {
                    var error = handler.GetMissingArgumentError(args) ??
                                GetDefaultMissingArgumentsError(args);
                    
                    ToolCallsInternal.Add(new AgentToolFrame.MissingArgs(toolCall.ToolId, error));
                }
            }
        }
    }

    private string GetDefaultToolHallucinationResult(string toolId)
    {
        // We will only mention the tool names to not blow up tokens:
        var toolList = string.Join(", ", Agent.ToolRegistry.ToolSet.Tools.Values.Select(x => x.ToolId));

        return $"Invalid tool `{toolId}. Available tools are: {toolList}`";
    }

    private string GetDefaultMissingArgumentsError(ArgumentExtractionResult args)
    {
        var missing = string.Join(", ", args.MissingArguments.Select(a => a.ArgumentName));

        return $"Missing required arguments {missing}";
    }
}