using KnowledgeSystem.Events.Api;

namespace KnowledgeSystem.Agents.Orchestration.RunnerEvents;

/// <summary>
///     Dispatched periodically while the agent is streaming a response, carrying the accumulated reasoning text of the current turn.
///     The last event of a turn always carries the full reasoning text.
/// </summary>
public record AgentThinkingEvent(string ThinkingText) : IEvent;
