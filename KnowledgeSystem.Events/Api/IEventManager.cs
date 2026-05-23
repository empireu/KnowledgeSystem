namespace KnowledgeSystem.Events.Api;

public interface IEventManager : IEventReceiverRegistry
{
    /// <summary>
    ///     Emits an event that will be forwarded to all subscribers.
    ///     They are called sequentially, based on their priority.
    /// </summary>
    /// <param name="event">The data transfer object. Must be immutable!</param>
    /// <param name="cancellationToken">Cancellation token forwarded to all subscribers.</param>
    /// <returns>A <see cref="ValueTask"/> that will complete when all the subscribers have returned.</returns>
    ValueTask SendAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent;
}