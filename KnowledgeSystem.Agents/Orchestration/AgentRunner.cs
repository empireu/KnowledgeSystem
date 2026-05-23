using System.Diagnostics.CodeAnalysis;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Orchestration.Observer;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using Microsoft.Extensions.AI;

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable UnusedAutoPropertyAccessor.Local

namespace KnowledgeSystem.Agents.Orchestration;

public abstract class AgentRunner
{
    /// <summary>
    ///     The observer for the whole execution tree, inherited from the parent.
    /// </summary>
    public IAgentObserver Observer { get; }
    
    /// <summary>
    ///     The chat client (either inherited from the parent, or a fresh one).
    /// </summary>
    public IChatClient Client { get; }
    
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
    
    protected AgentRunner(IAgentObserver observer, IChatClient client, AgentRunner? parent, ICompletionFactory? completionFactory)
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
        ChatOptions CreateOptionsForTurn(AgentRunner runner);
    }

    public sealed class DefaultCompletionFactory : ICompletionFactory
    {
        public static readonly DefaultCompletionFactory Instance = new();
        
        private DefaultCompletionFactory() { }
        
        public ChatOptions CreateOptionsForTurn(AgentRunner runner)
        {
            return new ChatOptions();
        }
    }
}

/// <summary>
///     Constructs an agent execution engine.
///     The engine is meant to execute a full turn, including all tool calls required for the turn.
/// </summary>
/// <typeparam name="TContext">Specific context class. Holds the chat history and specialized data for sub-agents.</typeparam>
public sealed class AgentRunner<TContext> : AgentRunner where TContext : AgentExecutionContext
{
    public readonly CancellationToken CancellationToken;

    /// <summary>
    ///     Constructs an agent execution engine.
    ///     The engine is meant to execute a full turn, including all tool calls required for the turn.
    /// </summary>
    /// <param name="observer">Event sink.</param>
    /// <param name="client">The chat client.</param>
    /// <param name="agent">The agent being executed.</param>
    /// <param name="parent">The parent execution engine, if this is a sub-agent.</param>
    /// <param name="context">The specific execution context.</param>
    /// <param name="cancellationToken"><b>Cancellation token that is valid throughout the lifetime of the runner (stored in various places).</b></param>
    /// <param name="completionFactory">Optional factory to configure the completion options.</param>
    /// <typeparam name="TContext">Specific context class. Holds the chat history and specialized data for sub-agents.</typeparam>
    public AgentRunner(IAgentObserver observer,
        IChatClient client,
        Agent<TContext> agent,
        AgentRunner? parent,
        TContext context,
        CancellationToken cancellationToken,
        ICompletionFactory? completionFactory = null) : base(observer, client, parent, completionFactory)
    {
        CancellationToken = cancellationToken;
        Agent = agent;
        ExecutionContext = context;
    }

    public override Agent<TContext> Agent { get; }

    public TContext ExecutionContext { get; }

    /// <summary>
    ///     Runs the agent to completion, looping <see cref="ExecuteTurn"/> until finished.
    /// </summary>
    public async Task RunAsync()
    {
        while (!IsFinished)
        {
            CancellationToken.ThrowIfCancellationRequested();
            await ExecuteTurn();
        }
    }
    
    /// <summary>
    ///     Executes one "turn". This is somewhat finely-grained; has multiple stop points (all the values in <see cref="AgentRunner.TurnStatus"/>).
    /// </summary>
    /// <returns></returns>
    public async Task<TurnStatus> ExecuteTurn()
    {
        var status = await ExecuteTurnCore();

        await Observer.OnTurnAsync(this, status, CancellationToken);
        
        return status;
    }

    private async Task<TurnStatus> ExecuteTurnCore()
    {
        if (IsFinished)
        {
            throw new Exception("Tried to execute finished runner!");
        }
        
        if (ToolCallsInternal.Count > 0)
        {
            if (await StepTools())
            {
                return TurnStatus.ToolsStepped;
            }

            var callbackStatus = await HandleCallbackResult(() => Agent.HandleToolFinish(this));
            ToolCallsInternal.Clear();
            return callbackStatus ?? TurnStatus.ToolsFinished;
        }
        
        var chatOptions = CompletionFactory.CreateOptionsForTurn(this);
        Agent.ToolRegistry.ToolSet.AddToOptions(chatOptions);
        
        // Executes the LLM call and raises the error and completion events:
        ChatResponse response;
        try
        {
            response = await Client.GetResponseAsync(
                ExecutionContext.ChatMessages,
                chatOptions,
                CancellationToken
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            FinishError = new AgentExecutionError($"Chat request failed: {ex.Message}", true);
            IsFinished = true;
            
            await Observer.OnErrorAsync(this, FinishError, CancellationToken);
            await Observer.OnAgentCompletedAsync(this, CancellationToken);
            
            return TurnStatus.CompletedWithError;
        }
        
        // Tool calls required:
        if (response.FinishReason == ChatFinishReason.ToolCalls)
        {
            ExecutionContext.InsertAssistantCompletion(response);
            var toolCalls = ChatMessageHelpers.GetFunctionCalls(response);
            await BeginToolCalls(response.Text, toolCalls);
            return TurnStatus.ToolCallsReceived;
        }
        
        // Non-tool completion. Continue with result and notify:
        await Observer.OnAssistantMessageAsync(this, response, CancellationToken);
        return await HandleCallbackResult(() => Agent.HandleCompletion(this, response)) ?? TurnStatus.CompletionHandled;
    }

    private async Task<TurnStatus?> HandleCallbackResult(Func<Task<AgentCallbackResult>> callback)
    {
        // Let the agent decide whether this finishes execution:
        AgentCallbackResult callbackResult;
        try
        {
            callbackResult = await callback();
        }
        catch (Exception ex)
        {
            callbackResult = new AgentCallbackResult(true, new AgentExecutionError($"Agent completion handler threw: {ex.Message}", true));
        }

        if (!callbackResult.CompletesExecution)
        {
            // Doesn't complete agent. Needs specific status:
            return null;
        }

        IsFinished = true;
        FinishError = callbackResult.Error;
        
        await Observer.OnAgentCompletedAsync(this, CancellationToken);

        return FinishError == null 
            ? TurnStatus.CompletedSuccessfully 
            : TurnStatus.CompletedWithError;
    } 

    /// <summary>
    ///     Steps the current tools.
    ///     For simple tools, this will await their execution.
    ///     For sub-agents, this will execute one turn.
    ///     Currently, the calls are executed sequentially. 
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
                case AgentToolFrame.PlainRunningFrame plainFrame:
                {
                    if (plainFrame.StartedTask == null)
                    {
                        var handler = (ToolHandler<TContext>.Plain) Agent.ToolRegistry.Handlers[plainFrame.Tool];

                        try
                        {
                            plainFrame.StartedTask = handler.ExecuteAsync(
                                this,
                                plainFrame.Args,
                                CancellationToken
                            );
                        }
                        catch (Exception ex)
                        {
                            var errorResult = new ToolExecutionResult(plainFrame.Tool, false, null, ex.Message, ex);
                            await Observer.OnToolResultAsync(this, indexInCollection, plainFrame.Tool, errorResult, CancellationToken);
                            plainFrame.Result = errorResult;
                            return true;
                        }

                        return true;
                    }
                    
                    ToolExecutionResult result;
                    try
                    {
                        result = await plainFrame.StartedTask;
                    }
                    catch (Exception ex)
                    {
                        result = new ToolExecutionResult(plainFrame.Tool, false, null, ex.Message, ex);
                    }
                    
                    await Observer.OnToolResultAsync(
                        this,
                        indexInCollection,
                        plainFrame.Tool,
                        result,
                        CancellationToken
                    );

                    plainFrame.Result = result;
                    
                    return true;
                }
                case AgentToolFrame.RunningSubAgent subAgentFrame:
                {
                    if (subAgentFrame.Proxy == null)
                    {
                        var handler = (ToolHandler<TContext>.SubAgent) Agent.ToolRegistry.Handlers[subAgentFrame.Tool];

                        try
                        {
                            subAgentFrame.Proxy = await handler.BeginSubAgentExecution(
                                this, 
                                subAgentFrame.Args,
                                ExecutionContext,
                                subAgentFrame.CallId,
                                CancellationToken
                            );
                        }
                        catch (Exception ex)
                        {
                            var errorResult = new ToolExecutionResult(subAgentFrame.Tool, false, null, ex.Message, ex);
                            await Observer.OnToolResultAsync(this, indexInCollection, subAgentFrame.Tool, errorResult, CancellationToken);
                            subAgentFrame.Result = errorResult;
                            return true;
                        }

                        return true;
                    }

                    ToolExecutionResult? result;
                    try
                    {
                        result = await subAgentFrame.Proxy.StepAsync();
                    }
                    catch (Exception ex)
                    {
                        result = new ToolExecutionResult(subAgentFrame.Tool, false, null, ex.Message, ex);
                    }

                    if (result != null)
                    {
                        await Observer.OnToolResultAsync(
                            this,
                            indexInCollection,
                            subAgentFrame.Tool,
                            result,
                            CancellationToken
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
                    ExecutionContext.InsertToolResult(hallucination.CallId, hallucination.ErrorMessage);
                    continue;
                case AgentToolFrame.MissingArgs missingArgs:
                    ExecutionContext.InsertToolResult(missingArgs.CallId,  missingArgs.ErrorMessage);
                    continue;
                case AgentToolFrame.RunningFrame running:
                    result = running.Result!;
                    break;
                default:
                    throw new Exception($"Invalid tool frame {toolCall}");
            }

            var output = result.IsSuccessful
                ? result.Output
                : result.FormatError();

            ExecutionContext.InsertToolResult(toolCall.CallId, output);
        }
        
        return false;
    }
    
    private readonly struct ToolCall
    {
        /// <summary>
        ///     The original call from the API.
        /// </summary>
        public required FunctionCallContent Call { get; init; }
        
        /// <summary>
        ///     The API call ID, used for writing back the history.
        /// </summary>
        public string CallId { get; init; }
        
        /// <summary>
        ///     The resolved tool, if the function name wasn't hallucinated.
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
    
    private async Task BeginToolCalls(string completion, List<FunctionCallContent> toolCalls)
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
                    CallId = toolCall.CallId,
                    Tool = tool,
                    OriginalIndex = callIndex,
                    Args = ArgumentExtractionResult.ExtractArguments(tool, toolCall.Arguments)
                });
            }
            else
            {
                calls.Add(new ToolCall
                {
                    Call = toolCall,
                    CallId = toolCall.CallId,
                    Tool = null,
                    OriginalIndex = callIndex,
                    Args = null
                });
            }
        }

        var toolEvents = new ToolCallInfo[calls.Count];
        
        // Matches all the events:
        for (var callIndex = 0; callIndex < calls.Count; callIndex++)
        {
            var toolCall = calls[callIndex];

            if (toolCall.Tool == null)
            {
                var functionName = toolCall.Call.Name;
                await Observer.OnErrorAsync(
                    this,
                    new AgentToolHallucinationError(
                        $"Invalid tool \"{functionName}\"", 
                        false,
                        functionName, 
                        callIndex
                    ),
                    CancellationToken
                );
            }
            else
            {
                var args = toolCall.Args!;
                
                if (args.Status == ArgumentExtractionResult.ExtractionStatus.Success)
                {
                    toolEvents[callIndex] = new ToolCallInfo
                    {
                        IsValid = true,
                        Tool = toolCall.Tool,
                        Args = args
                    };
                }
                else
                {
                    await Observer.OnErrorAsync(
                        this,
                        new AgentToolIncompleteArgumentsError(
                            $"Missing arguments for \"{toolCall.Tool.ToolId}\"", 
                            false,
                            toolCall.Tool.ToolId, 
                            callIndex,
                            args
                        ),
                        CancellationToken
                    );
                }
            }
        }

        // Dispatches the events, along with the completion:
        await Observer.OnToolCallsAsync(
            this,
            completion,
            toolEvents.ToArray(),
            CancellationToken
        );
        
        // Pushes each call to the pending list.
        // Does not start their execution yet, but it does resolve the immediate errors.
        for (var callIndex = 0; callIndex < calls.Count; callIndex++)
        {
            var toolCall = calls[callIndex];

            if (toolCall.Tool == null)
            {
                var functionName = toolCall.Call.Name;
                var message = Agent.GetToolHallucinationError(functionName) ??
                              GetDefaultToolHallucinationResult(functionName);
              
                ToolCallsInternal.Add(new AgentToolFrame.Hallucination(toolCall.CallId, functionName, message));
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
                            ToolCallsInternal.Add(new AgentToolFrame.PlainRunningFrame(toolCall.CallId, tool, args));
                            break;
                        case ToolHandler<TContext>.SubAgent:
                            ToolCallsInternal.Add(new AgentToolFrame.RunningSubAgent(toolCall.CallId, tool, args));
                            break;
                        default:
                            throw new Exception($"Invalid tool handler {handler}");
                    }
                }
                else
                {
                    var error = handler.GetMissingArgumentError(args) ??
                                GetDefaultMissingArgumentsError(args);
                    
                    ToolCallsInternal.Add(new AgentToolFrame.MissingArgs(toolCall.CallId, error));
                }
            }
        }
    }

    private string GetDefaultToolHallucinationResult(string functionName)
    {
        // We will only mention the tool names to not blow up tokens:
        var toolList = string.Join(", ", Agent.ToolRegistry.ToolSet.Tools.Values.Select(x => x.ToolId));

        return $"Invalid tool `{functionName}`. Available tools are: {toolList}";
    }

    private static string GetDefaultMissingArgumentsError(ArgumentExtractionResult args)
    {
        var missing = string.Join(", ", args.MissingArguments.Select(a => a.ArgumentName));

        return $"Missing required arguments {missing}";
    }

    #region Helper

    public bool TryGetUniqueActiveHandlerOfType<THandler>([NotNullWhen(true)] out THandler? result) where THandler : ToolHandler
    {
        result = null;

        for (var index = 0; index < ToolCallsInternal.Count; index++)
        {
            var agentToolFrame = ToolCallsInternal[index];

            if (agentToolFrame is not AgentToolFrame.RunningFrame runningFrame)
            {
                continue;
            }

            if (Agent.ToolRegistry.Handlers[runningFrame.Tool] is not THandler handler)
            {
                continue;
            }
            
            if (result != null)
            {
                throw new InvalidOperationException($"Tried to get unique active handler of type {typeof(THandler)}, but found duplicates");
            }

            result = handler;
        }
        
        return result != null;
    }

    public bool TryGetUniqueActiveSubAgentProxyForHandler<THandler>([NotNullWhen(true)] out ISubAgentProxy? result) where THandler : ToolHandler
    {
        result = null;
        
        var foundHandler = false;
        for (var index = 0; index < ToolCallsInternal.Count; index++)
        {
            var agentToolFrame = ToolCallsInternal[index];

            if (agentToolFrame is not AgentToolFrame.RunningSubAgent subAgentFrame)
            {
                continue;
            }

            if (Agent.ToolRegistry.Handlers[subAgentFrame.Tool] is not THandler handler)
            {
                continue;
            }
            
            if (foundHandler)
            {
                throw new InvalidOperationException($"Tried to get unique active handler of type {typeof(THandler)}, but found duplicates");
            }

            foundHandler = true;
            result = subAgentFrame.Proxy ?? throw new InvalidOperationException($"Tried to get proxy for {handler} before it was available");
        }
        
        return result != null;
    }
    
    #endregion
}
