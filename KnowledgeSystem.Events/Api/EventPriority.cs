namespace KnowledgeSystem.Events.Api;

/// <summary>
///     Event priority, used to order methods that are listening for the same event.
/// </summary>
public enum EventPriority
{
    Lowest = -2,
    Low = -1,
    Normal = 0,
    High = 1,
    VeryHigh = 2,
    RealTime = 3
}