namespace KnowledgeSystem.Api;

public sealed class ActiveRunInfo
{
    public required CancellationTokenSource Cts { get; init; }
        
    public Func<CancellationToken, Task>? OnCloseAction { get; init; }
}