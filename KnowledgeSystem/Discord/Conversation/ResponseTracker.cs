using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;

namespace KnowledgeSystem.Discord.Conversation;

public class ResponseTracker : IHostedService
{
    public sealed class ActiveRunInfo
    {
        public required CancellationTokenSource Cts { get; init; }
        
        public Func<CancellationToken, Task>? OnCloseAction { get; init; }
    }
    
    private readonly ConcurrentDictionary<ulong, ActiveRunInfo> _activeRuns = new();

    public void Add(ulong key, ActiveRunInfo cts)
    {
        _activeRuns[key] = cts;
    }

    public void Remove(ulong key)
    {
        _activeRuns.TryRemove(key, out _);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var activeRunInfo in _activeRuns.Values)
        {
            await activeRunInfo.Cts.CancelAsync();

            if (activeRunInfo.OnCloseAction != null)
            {
                await activeRunInfo.OnCloseAction(cancellationToken);
            }
        }
    }
}
