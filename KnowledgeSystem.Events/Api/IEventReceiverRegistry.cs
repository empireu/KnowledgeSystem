namespace KnowledgeSystem.Events.Api;

public interface IEventReceiverRegistry
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
}