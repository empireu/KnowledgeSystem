using KnowledgeSystem.Agents.Tools;

namespace KnowledgeSystem.Agents.Orchestration.Observer;

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