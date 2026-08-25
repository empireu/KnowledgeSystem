namespace KnowledgeSystem.Events.Api;

/// <summary>
///     Dispatched when the agent queues a file to be attached to the final response message.
/// </summary>
public record AddAttachmentEvent(string Name, string Content) : IEvent;
