using KnowledgeSystem.Events.Api;

namespace KnowledgeSystem.Events.Implementation;

public sealed class NullEventManager : IEventManager
{
    public static readonly NullEventManager Instance = new();
    
    public sealed class NullReceiverHandle : IDisposable
    {
        public static readonly NullReceiverHandle Instance = new();
        
        private NullReceiverHandle()
        {
            
        }
        
        public void Dispose()
        {
            // Ignored
        }
    }
    
    private NullEventManager()
    {
        
    }

    public IDisposable AddReceiver<TListener>(TListener listener) where TListener : IEventReceiver
    {
        return NullReceiverHandle.Instance;
    }

    public ValueTask SendAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        return ValueTask.CompletedTask;
    }
}