// ReSharper disable UnusedMember.Global

namespace KnowledgeSystem.Events.Api;

/// <summary>
///     Used to mark a method for receiving events.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class SubscribeEvent : Attribute
{
    public SubscribeEvent(EventPriority priority = EventPriority.Normal)
    {
        Priority = priority;
    }

    public SubscribeEvent(Type eventType, EventPriority priority = EventPriority.Normal)
    {
        Priority = priority;
        EventType = eventType;
    }

    public EventPriority Priority { get; set; }

    public Type? EventType { get; set; }

    /// <summary>
    ///     If true, an exception in this handler propagates out of <see cref="IEventManager.SendAsync"/> and stops the event loop.
    ///     If false, the exception is caught and logged, and the next handler runs.
    /// </summary>
    public bool IsCritical { get; set; } = true;
}