namespace KnowledgeSystem.Agents.Orchestration;

/// <summary>
///     Represents the state of a running agent.
/// </summary>
/// <param name="parentExecutionContext"></param>
public class AgentExecutionContext(AgentExecutionContext? parentExecutionContext)
{
    /// <summary>
    ///     The parent context. Will be null for the main orchestration agent.
    /// </summary>
    public AgentExecutionContext? ParentExecutionContext { get; } = parentExecutionContext;
}