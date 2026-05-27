using KnowledgeSystem.Api;
using Microsoft.Extensions.Hosting;

namespace KnowledgeSystem.Discord.Conversation;

public class ResponseTracker : IResponseTracker, IHostedService
{
    private readonly Dictionary<ulong, ActiveRunInfo> _activeRuns = new();
    private readonly Lock _lock = new();
    private bool _invalid;
    
    public void Add(ulong key, ActiveRunInfo info)
    {
        lock (_lock)
        {
            if (_invalid)
            {
                throw new InvalidOperationException("Run tracker is already shutting down");
            }
            
            if (!_activeRuns.TryAdd(key, info))
            {
                throw new InvalidOperationException($"Duplicate run info with ID {key}");
            }
        }
    }

    public bool Remove(ulong key)
    {
        lock (_lock)
        {
            if (_invalid)
            {
                // Seems like a good path for now:
                return _activeRuns.ContainsKey(key);
            }
            
            return _activeRuns.Remove(key);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        List<ActiveRunInfo> activeRuns;
        
        lock (_lock)
        {
            if (_invalid)
            {
                return;
            }
            
            _invalid = true;
            
            activeRuns = _activeRuns.Values.ToList();
        }
        
        foreach (var activeRunInfo in activeRuns)
        {
            await activeRunInfo.Cts.CancelAsync();

            if (activeRunInfo.OnCloseAction != null)
            {
                await activeRunInfo.OnCloseAction(cancellationToken);
            }
        }
    }
}
