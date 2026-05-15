using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;

namespace KnowledgeSystem.Agents.Orchestration.Observer;

/// <summary>
///     Observer for the execution of an agent and its subagents, meant for display.
/// </summary>
public interface IAgentObserver
{
    /// <summary>
    ///     Called when the agent runs tools with all required arguments. Multiple tools can be called per round, so this may be called multiple times.
    ///     If the agent hallucinates a tool, <see cref="OnErrorAsync"/> will be called with a <see cref="AgentToolHallucinationError"/>.
    ///
    /// </summary>
    Task OnToolCallAsync(AgentRunner runner, ToolCallInfo info, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     Called when the execution of a tool finishes. Always comes after the initial <see cref="OnToolCallAsync"/> passes the tool calls.
    /// </summary>
    Task OnToolResultAsync(AgentRunner runner, int indexInCollection, AgentTool tool, ToolExecutionResult result, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     Called when the agent outputs a message.
    /// </summary>
    Task OnAssistantMessageAsync(AgentRunner runner, string message, CancellationToken cancellationToken) => Task.CompletedTask;
    
    /// <summary>
    ///     Called when the agent finishes its execution.
    /// </summary>
    Task OnAgentCompletedAsync(AgentRunner runner, CancellationToken cancellationToken) => Task.CompletedTask;
    
    /// <summary>
    ///     Called when an error occurs during execution.
    /// </summary>
    Task OnErrorAsync(AgentRunner runner, AgentExecutionError error, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class NullAgentObserver : IAgentObserver
{
    public static readonly  NullAgentObserver Instance = new();
    
    private NullAgentObserver() { }
}