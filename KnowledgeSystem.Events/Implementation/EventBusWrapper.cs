using KnowledgeSystem.Events.Api;

namespace KnowledgeSystem.Events.Implementation;

internal class EventBusWrapper(IEventBus wrapAround, object o) : IEventBus
{
    public Type EventType => wrapAround.EventType;

    public EventPriority Priority => wrapAround.Priority;

    public bool IsCritical => wrapAround.IsCritical;

    public string Method => wrapAround.Method;

    public ValueTask InvokeAsync(object? eventHandler, object @event, IServiceProvider provider, CancellationToken cancellationToken = default)
    {
        return wrapAround.InvokeAsync(o, @event, provider, cancellationToken);
    }
}