namespace KnowledgeSystem.Api;

public sealed class ActiveRunInfo
{
    public required CancellationTokenSource Cts { get; init; }
}