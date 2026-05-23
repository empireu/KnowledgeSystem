using System.Collections.Concurrent;

namespace KnowledgeSystem.Events.Implementation;

internal sealed class DeferredCallbackList 
{
    private sealed class UnregisterEvent(DeferredCallbackList register, int id) : IDisposable
    {
        public void Dispose()
        {
            register.Remove(id);
        }
    }

    private readonly ConcurrentDictionary<int, IEventBus> _callbacks = new();
    private int _id;

    public IEnumerable<IEventBus> GetBusCollection()
    {
        return _callbacks.Select(x => x.Value);
    }

    public IDisposable Add(IEventBus callback)
    {
        var id = Interlocked.Increment(ref _id);

        if (!_callbacks.TryAdd(id, callback))
        {
            Environment.FailFast("Failed");
        }

        return new UnregisterEvent(this, id);
    }

    private void Remove(int id)
    {
        _callbacks.TryRemove(id, out _);
    }
}