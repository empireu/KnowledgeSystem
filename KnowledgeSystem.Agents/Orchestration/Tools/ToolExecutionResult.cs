using KnowledgeSystem.Agents.Tools;

namespace KnowledgeSystem.Agents.Orchestration.Tools;

/// <summary>
///     Result of executing a tool.
/// </summary>
public sealed class ToolExecutionResult
{
    /// <summary>
    ///     Result of executing a tool.
    /// </summary>
    public ToolExecutionResult(AgentTool tool, Status status, string? result, string? errorMessage, Exception? thrownException)
    {
        Result = result;
        ErrorMessage = errorMessage;
        ThrownException = thrownException;
        Tool = tool;
        ExecutionStatus = status;
    }

    public enum Status
    {
        /// <summary>
        ///     The tool executed successfully.
        /// </summary>
        Success,
        /// <summary>
        ///     The tool's logic produced an error, or the arguments were invalid.
        /// </summary>
        Error
    }

    public AgentTool Tool { get; }
    
    /// <summary>
    ///     The final status of the tool.
    /// </summary>
    public Status ExecutionStatus { get; }
    
    public string Result => ExecutionStatus == Status.Success
        ? field ?? string.Empty 
        : throw new InvalidOperationException($"Cannot get tool result for {ExecutionStatus}"); 
    
    /// <summary>
    ///     The formatted error message, usually when the error is well-defined by the tool.
    /// </summary>
    public string? ErrorMessage => ExecutionStatus == Status.Error
        ? field
        : throw new InvalidOperationException($"Cannot get error message for {ExecutionStatus}");
   
    /// <summary>
    ///     If the backend caught an exception during the execution of the agent, it will show up here.
    ///     Should never happen due to resource leaks that could happen.
    /// </summary>
    public Exception? ThrownException => ExecutionStatus == Status.Error
        ? field
        : throw new InvalidOperationException($"Cannot get exception for {ExecutionStatus}");
}