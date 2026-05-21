using KnowledgeSystem.Agents.Orchestration.Tools;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Agents.Orchestration;

public abstract class Agent(string agentId)
{
    public string AgentId { get; } = agentId;
}

/// <summary>
///     An agent, supplied with tools.
/// </summary>
/// <param name="agentId"></param>
/// <typeparam name="TContext">The context class for the agent.</typeparam>
public abstract class Agent<TContext>(string agentId) : Agent(agentId) where TContext : AgentExecutionContext 
{
    public AgentToolRegistry<TContext> ToolRegistry { get; } = new();

    /// <summary>
    ///     Called when a completion arrives, that isn't a tool call.
    ///     Completion should be done on the runner if needed.
    /// </summary>
    public virtual Task<AgentCallbackResult> HandleCompletion(AgentRunner<TContext> runner, ChatResponse response)
    {
        return Task.FromResult(AgentCallbackResult.Break);
    }

    /// <summary>
    ///     Called when the tools finish.
    /// </summary>
    public virtual Task<AgentCallbackResult> HandleToolFinish(AgentRunner<TContext> runner)
    {
        return Task.FromResult(AgentCallbackResult.Continue);
    }
    
    /// <summary>
    ///     Called when the LLM calls a tool that doesn't exist.
    ///     Return the message to insert as the tool result, or null to use the default ("Invalid tool!").
    /// </summary>
    public virtual string? GetToolHallucinationError(string toolName) => null;
}