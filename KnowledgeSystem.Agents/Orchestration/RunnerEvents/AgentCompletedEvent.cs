using KnowledgeSystem.Events.Api;

namespace KnowledgeSystem.Agents.Orchestration.RunnerEvents;

/// <summary>
///     Dispatched when the agent finishes its execution.
/// </summary>
public record AgentCompletedEvent : IEvent;