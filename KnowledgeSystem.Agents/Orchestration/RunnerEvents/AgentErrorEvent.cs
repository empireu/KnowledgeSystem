using KnowledgeSystem.Events.Api;

namespace KnowledgeSystem.Agents.Orchestration.RunnerEvents;

/// <summary>
///     Called when an error occurs during execution.
/// </summary>
public record AgentErrorEvent(AgentExecutionError Error) : IEvent;