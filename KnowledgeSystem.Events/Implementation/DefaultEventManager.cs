using System.Collections.Concurrent;
using KnowledgeSystem.Events.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Events.Implementation;

public class DefaultEventManager(ILogger<DefaultEventManager>? errorLogger, IServiceProvider? serviceProvider) : IEventManager
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? EmptyServiceProvider.Instance;
   
    private sealed class DisposeList : IDisposable
    {
        public List<IDisposable> Disposables { get; } = [];

        public void Dispose()
        {
            foreach (var disposable in Disposables)
            {
                disposable.Dispose();
            }
        }
    }

    private readonly struct EventHandler(IEventReceiver? o, IEventBus eventBus)
    {
        public IEventReceiver? Object { get; } = o;

        public IEventBus EventBus { get; } = eventBus;

        public void Deconstruct(out IEventReceiver? o, out IEventBus eventBus)
        {
            o = Object;
            eventBus = EventBus;
        }
    }

    private readonly ConcurrentDictionary<Type, DeferredCallbackList> _temporaryEventListeners = new();
    private readonly ConcurrentDictionary<Type, List<EventHandler>> _sortedHandlers = new();

    public IDisposable AddReceiver<TListener>(TListener listener) where TListener : IEventReceiver
    {
        var disposeList = new DisposeList();
        var listeners = ExpressionEventBus.FromType(typeof(TListener));

        foreach (var eventExpressionListener in listeners)
        {
            var wrapper = new EventBusWrapper(eventExpressionListener, listener);
            var register = _temporaryEventListeners.GetOrAdd(wrapper.EventType,
                _ => new DeferredCallbackList());
            disposeList.Disposables.Add(register.Add(wrapper));
        }

        if (listeners.Count > 0)
        {
            _sortedHandlers.TryRemove(typeof(TListener), out _);
        }

        return disposeList;
    }

    private List<EventHandler> SortHandlers<TEvent>() where TEvent : IEvent
    {
        var handlers = GetHandlers<TEvent>()
            .OrderByDescending(e => e.EventBus.Priority)
            .ToList();

        _sortedHandlers[typeof(TEvent)] = handlers;

        return handlers;
    }

    public virtual async ValueTask SendAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        if (!_sortedHandlers.TryGetValue(typeof(TEvent), out var handlers))
        {
            handlers = SortHandlers<TEvent>();
        }

        foreach (var (handler, eventListener) in handlers)
        {
            if (errorLogger == null || eventListener.IsCritical)
            {
                await eventListener.InvokeAsync(handler, @event, _serviceProvider, cancellationToken);
            }
            else
            {
                try
                {
                    await eventListener.InvokeAsync(handler, @event, _serviceProvider, cancellationToken);
                }
                catch (Exception e)
                {
                    errorLogger.LogError("Non-critical handler {method} failed for {type}: {ex}", eventListener.Method, typeof(TEvent), e);
                }
            }
        }
    }

    private IEnumerable<EventHandler> GetHandlers<TEvent>() where TEvent : IEvent
    {
        var eventType = typeof(TEvent);
        var interfaces = eventType.GetInterfaces();

        foreach (var @interface in interfaces)
        {
            if (_temporaryEventListeners.TryGetValue(@interface, out var callbackList))
            {
                foreach (var eventListener in callbackList.GetBusCollection())
                {
                    yield return new EventHandler(null, eventListener);
                }
            }
        }

        foreach (var handler in _serviceProvider.GetServices<IEventReceiver>())
        {
            var events = ExpressionEventBus.FromType(handler.GetType());

            foreach (var eventHandler in events)
            {
                if (eventHandler.EventType != typeof(TEvent) && !interfaces.Contains(eventHandler.EventType))
                {
                    continue;
                }

                yield return new EventHandler(handler, eventHandler);
            }
        }

        if (_temporaryEventListeners.TryGetValue(eventType, out var cl2))
        {
            foreach (var eventListener in cl2.GetBusCollection())
            {
                yield return new EventHandler(null, eventListener);
            }
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        private EmptyServiceProvider() { }

        public object? GetService(Type serviceType)
        {
            if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var elementType = serviceType.GetGenericArguments()[0];
                return Array.CreateInstance(elementType, 0);
            }

            return null;
        }
    }
}