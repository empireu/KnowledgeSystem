using KnowledgeSystem.Agents.Tools;

namespace KnowledgeSystem.Agents.Orchestration;

/// <summary>
///     Base type for agent errors, which can include LLM-related errors, tool-call related errors or other internal errors.
/// </summary>
public class AgentExecutionError(string message, bool isCritical)
{
    /// <summary>
    ///     A small, descriptive message for users.
    /// </summary>
    public string Message { get; } = message;
    
    /// <summary>
    ///     If true, this error will finish the execution of the agent. Otherwise, the agent will try to handle it.
    /// </summary>
    public bool IsCritical { get; } = isCritical;
    
    public override string ToString()
    {
        return Message;
    }
}

public abstract class AgentToolError(string message, bool isCritical, string toolId, int index) : AgentExecutionError(message, isCritical)
{
    /// <summary>
    ///     The hallucinated tool ID.
    /// </summary>
    public string ToolId { get; } = toolId;
    
    /// <summary>
    ///     The index in the completion's tool calls.
    /// </summary>
    public int Index { get; } = index;
}

/// <summary>
///     Error raised when the LLM tries to call a tool, but the tool ID doesn't resolve to the registered tools.
/// </summary>
public sealed class AgentToolHallucinationError(
    string message,
    bool isCritical,
    string toolId,
    int index
) : AgentToolError(message, isCritical, toolId, index);

/// <summary>
///     Error raised when the LLM tries to call a tool, but it didn't set all required args.
/// </summary>
public sealed class AgentToolIncompleteArgumentsError(
    string message,
    bool isCritical,
    string toolId,
    int index,
    ArgumentExtractionResult extractionResult) : AgentToolError(message, isCritical, toolId, index)
{
    public ArgumentExtractionResult ExtractionResult { get; } = extractionResult;
}

/// <summary>
///     Error that occurred due to misuse of a tool.
/// </summary>
public class AgentToolExecutionError(string message, bool isCritical, AgentTool tool, int index) : AgentExecutionError(message, isCritical)
{
    /// <summary>
    ///     The tool that produced the error.
    /// </summary>
    public AgentTool Tool { get; } = tool;

    /// <summary>
    ///     The index in the original array.
    /// </summary>
    public int Index { get; } = index;
}