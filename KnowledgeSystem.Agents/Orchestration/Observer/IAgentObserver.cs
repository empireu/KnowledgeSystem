namespace KnowledgeSystem.Agents.Orchestration.Observer;

/// <summary>
///     Observer for the execution of an agent, meant for display.
/// </summary>
public interface IAgentObserver
{
    /// <summary>
    ///     Called when the agent outputs a thinking trace.
    /// </summary>
    Task OnThinkingAsync(string trace, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     Called when the agent runs tools. Multiple tools can be called per round.
    /// </summary>
    Task OnToolCallAsync(ToolCallInfo[] toolCalls, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     Called when the execution of a tool finishes. Always comes after the initial <see cref="OnToolCallAsync"/> passes the tool calls.
    /// </summary>
    Task OnToolResultAsync(ToolCallResult result, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     Called when the agent outputs a message.
    /// </summary>
    Task OnAssistantMessageAsync(string message, CancellationToken cancellationToken) => Task.CompletedTask;
    
    /// <summary>
    ///     Called when the agent finishes its execution.
    /// </summary>
    Task OnAgentCompletedAsync(AgentExecutionResult result, CancellationToken cancellationToken) => Task.CompletedTask;
    
    /// <summary>
    ///     Called when an error occurs during execution.
    /// </summary>
    /// <param name="error"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task OnErrorAsync(AgentExecutionError error, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class NullAgentObserver : IAgentObserver
{
    public static readonly  NullAgentObserver Instance = new();
    
    private NullAgentObserver() { }
}