using KnowledgeSystem.Agents.Tools;

namespace KnowledgeSystem.Agents.Orchestration.Tools;

public abstract class ToolHandler(AgentTool tool)
{
    public AgentTool Tool { get; } = tool;
}

/// <summary>
///     Handler for a specific agent tool.
/// </summary>
public abstract class ToolHandler<TContext>(AgentTool tool) : ToolHandler(tool) where TContext : class
{
    /// <summary>
    ///     Called when the agent executes the tool.
    ///     Errors should all be handled and reported in the <see cref="ToolExecutionResult"/>.
    /// </summary>
    public abstract Task<ToolExecutionResult> ExecuteAsync(ArgumentExtractionResult args, TContext runContext, CancellationToken cancellationToken);
}