using KnowledgeSystem.Events.Api;

namespace KnowledgeSystem.Agent.AgentEvents;

/// <summary>
///     Dispatched when the agent produces a message that passed peer-review.
/// </summary>
public record AgentPeerReviewedMessageEvent(string Content) : IEvent;