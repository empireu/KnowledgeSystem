namespace KnowledgeSystem.Agents.Orchestration;

public sealed class AgentCallbackResult(bool completesExecution, AgentExecutionError? error)
{
    public static readonly AgentCallbackResult Continue = new(false, null);
    public static readonly AgentCallbackResult Break = new(true, null);
    
    /// <summary>
    ///     If true, this will complete the execution.
    /// </summary>
    public bool CompletesExecution { get; } = completesExecution;
    
    /// <summary>
    ///     If the agent completed with an error, this will hold the error.
    /// </summary>
    public AgentExecutionError? Error { get; } = error;
    
    public static AgentCallbackResult Throw(AgentExecutionError error)
    {
        return error == null 
            ? throw new ArgumentException("Cannot construct agent callback result with null error", nameof(error)) 
            : new AgentCallbackResult(true, error);
    } 
}