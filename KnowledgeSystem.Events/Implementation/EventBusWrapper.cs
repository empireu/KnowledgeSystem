using KnowledgeSystem.Events.Api;

namespace KnowledgeSystem.Events.Implementation;

internal class EventBusWrapper : IEventBus
{
    private readonly IEventBus _wrapAround;
    private readonly object _object;

    public EventBusWrapper(IEventBus wrapAround, object o)
    {
        _wrapAround = wrapAround;
        _object = o;
    }

    public Type EventType => _wrapAround.EventType;

    public EventPriority Priority => _wrapAround.Priority;

    public ValueTask InvokeAsync(object? eventHandler, object @event, IServiceProvider provider)
    {
        return _wrapAround.InvokeAsync(_object, @event, provider);
    }
}