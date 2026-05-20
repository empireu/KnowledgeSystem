using KnowledgeSystem.Agents.Tools;

namespace KnowledgeSystem.Agents.Orchestration.Tools;

public abstract class ToolHandler(AgentTool tool)
{
    public AgentTool Tool { get; } = tool;
    
    /// <summary>
    ///     Called when the LLM calls this tool but omits required arguments.
    ///     Return the message to insert as the tool result, or null to use the default <c>"Error: missing required arguments: X, Y.</c>
    /// </summary>
    public virtual string? GetMissingArgumentError(ArgumentExtractionResult args) => null;
    
    public ToolExecutionResult Success(string output) => new(Tool, true, output, null, null);
    
    public ToolExecutionResult Error(string message) => new(Tool, false, null, message, null);
}

/// <summary>
///     Handler for a specific agent tool.
/// </summary>
public abstract class ToolHandler<TContext> : ToolHandler where TContext : AgentExecutionContext
{
    internal ToolHandler(AgentTool tool) : base(tool) { }

    /// <summary>
    ///     Handler for non-sub-agent tools.
    /// </summary>
    public abstract class Plain(AgentTool tool) : ToolHandler<TContext>(tool)
    {
        /// <summary>
        ///     Called when the agent executes the tool. Meant to begin the execution of the tool.
        ///     The task will be awaited next turn.
        ///     Errors should all be handled and reported in the <see cref="ToolExecutionResult"/>.
        /// </summary>
        public abstract Task<ToolExecutionResult> ExecuteAsync(AgentRunner<TContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken);
    }

    public abstract class SubAgent(AgentTool tool) : ToolHandler<TContext>(tool)
    {
        /// <summary>
        ///     Called when the agent executes the tool. Meant to begin the execution of the tool.
        ///     The task will be awaited next turn.
        ///     Errors should all be handled and reported in the <see cref="ToolExecutionResult"/>.
        /// </summary>
        public abstract Task<ISubAgentProxy> BeginSubAgentExecution(AgentRunner<TContext> runner, ArgumentExtractionResult args, TContext runContext, string toolCallId, CancellationToken cancellationToken);
    }
}

/// <summary>
///     Proxy for stepping a sub-agent's execution.
///     Implementations must not throw from <see cref="StepAsync"/>.
///     Return a <see cref="ToolExecutionResult"/> with <c>IsSuccessful = false</c> instead.
/// </summary>
public interface ISubAgentProxy
{
    AgentRunner AgentRunner { get; }

    /// <summary>
    ///     Steps the sub-agent. Returns null if the sub-agent is still running, or the final <see cref="ToolExecutionResult"/> when done.
    ///     Must not throw; return an error result instead.
    /// </summary>
    Task<ToolExecutionResult?> StepAsync();
}
