using KnowledgeSystem.Events.Api;

namespace KnowledgeSystem.Agents.Orchestration.RunnerEvents;

/// <summary>
///     Dispatched when the runner completes a turn.
/// </summary>
public record AgentTurnEvent(AgentRunner.TurnStatus TurnStatus) : IEvent;