using KnowledgeSystem.Events.Api;

namespace KnowledgeSystem.Agent.Events;

/// <summary>
///     Dispatched when the agent produces a message that passed peer-review.
/// </summary>
public record AgentPeerReviewedMessageEvent(string Content) : IEvent;