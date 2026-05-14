namespace KnowledgeSystem.Agents.Orchestration.Tools;

public sealed class AgentVoidResult
{
    public static readonly AgentVoidResult Instance = new();
    
    private AgentVoidResult() { }
}