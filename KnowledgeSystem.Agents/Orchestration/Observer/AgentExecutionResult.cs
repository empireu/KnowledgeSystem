namespace KnowledgeSystem.Agents.Orchestration.Observer;

/// <summary>
///     The final result of execution for an agent; the agent is done when this is posted.
/// </summary>
/// <param name="status"></param>
public class AgentExecutionResult(AgentExecutionResult.Status status)
{
    public enum Status
    {
        /// <summary>
        ///     The agent's execution finished successfully.
        /// </summary>
        FinishedSuccessfully,
        /// <summary>
        ///     The agent's execution was canceled externally.
        /// </summary>
        Canceled,
        /// <summary>
        ///     The agent's execution finished due to error.
        /// </summary>
        Error
    }

    public Status FinalStatus { get; } = status;
}