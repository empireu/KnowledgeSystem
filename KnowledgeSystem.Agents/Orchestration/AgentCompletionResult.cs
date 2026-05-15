namespace KnowledgeSystem.Agents.Orchestration;

public sealed class AgentCompletionResult(bool completesExecution, AgentExecutionError? error)
{
    /// <summary>
    ///     If true, this will complete the execution.
    /// </summary>
    public bool CompletesExecution { get; } = completesExecution;
    
    /// <summary>
    ///     If the agent completed with an error, this will hold the error.
    /// </summary>
    public AgentExecutionError? Error { get; } = error;
}