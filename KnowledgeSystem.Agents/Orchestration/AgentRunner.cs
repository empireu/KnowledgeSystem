namespace KnowledgeSystem.Agents.Orchestration;

public class AgentRunner<TContext, TResult>(TContext context) 
    where TContext : AgentExecutionContext 
    where TResult : class
{
    public TContext ExecutionContext { get; } = context;
    public bool Completed { get; private set; }

    public async Task ExecuteTurn()
    {
        
    }
}