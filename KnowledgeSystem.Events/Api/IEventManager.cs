namespace KnowledgeSystem.Events.Api;

public interface IEventManager
{
    /// <summary>
    ///     Used to register a class instance for receiving events.
    ///     In order to be useful, the class needs to have void or ValueTask methods with the event as an argument.
    ///     They may contain arguments for any types in the dependency container.
    ///     They must also be annotated with the <see cref="SubscribeEvent"/> attribute.
    /// </summary>
    /// <param name="listener">An instance of the listener.</param>
    /// <returns>De-registration disposable.</returns>
    IDisposable AddReceiver<TListener>(TListener listener) where TListener : IEventReceiver;

    /// <summary>
    ///     Emits an event that will be forwarded to all subscribers.
    ///     They are called sequentially, based on their priority.
    /// </summary>
    /// <param name="event">The data transfer object. Must be immutable!</param>
    /// <returns>A <see cref="ValueTask"/> that will complete when all the subscribers have returned.</returns>
    ValueTask SendAsync<TEvent>(TEvent @event) where TEvent : IEvent;
}