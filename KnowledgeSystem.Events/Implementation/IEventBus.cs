using KnowledgeSystem.Events.Api;

namespace KnowledgeSystem.Events.Implementation;

internal interface IEventBus
{
    Type EventType { get; }

    EventPriority Priority { get; }

    bool IsCritical { get; }

    string Method { get; }

    ValueTask InvokeAsync(object? eventHandler, object @event, IServiceProvider provider, CancellationToken cancellationToken = default);
}