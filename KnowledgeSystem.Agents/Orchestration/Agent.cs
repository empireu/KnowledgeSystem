using KnowledgeSystem.Agents.Orchestration.Tools;
using OpenAI.Chat;

namespace KnowledgeSystem.Agents.Orchestration;

public abstract class ChatAgent(string agentId)
{
    public string AgentId { get; } = agentId;
}

/// <summary>
///     An agent, supplied with tools.
/// </summary>
/// <param name="agentId"></param>
/// <typeparam name="TContext">The context class for the agent.</typeparam>
/// <typeparam name="TResult"></typeparam>
public abstract class Agent<TContext, TResult>(string agentId) : ChatAgent(agentId), IDisposable
    where TContext : AgentExecutionContext 
    where TResult : class 
{
    public AgentToolRegistry<TContext> ToolRegistry { get; } = new();

    /// <summary>
    ///     Called when a completion arrives, that isn't a tool call.
    /// </summary>
    public abstract Task<AgentCompletionResult<TResult>> CompleteAsync(ChatCompletion completion);

    /// <summary>
    ///     Called after execution ended due to errors or when <see cref="CompleteAsync"/> reported finish.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}