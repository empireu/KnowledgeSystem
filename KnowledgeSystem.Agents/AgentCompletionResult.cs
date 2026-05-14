namespace KnowledgeSystem.Agents;

/// <summary>
///     The result of executing a non-tool-call turn by the agent.
/// </summary>
public sealed class AgentCompletionResult<TResult>(bool finishesAgent, TResult? result) where TResult : class
{
    /// <summary>
    ///     If true, the agent's execution has been completed.
    /// </summary>
    public bool FinishesAgent { get; } = finishesAgent;

    /// <summary>
    ///     The result passed by the agent.
    /// </summary>
    public TResult? Result { get; } = result;
}