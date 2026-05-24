using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;

namespace KnowledgeSystem.Agent.Tools.Review;

public class ReviewFlagToolHandler<TContext>(AgentTool tool, Func<TContext, string> resultProvider) : ToolHandler<TContext>.Plain(tool) where TContext : ConversationalContext
{
    public static void Register(AgentToolRegistry<TContext> registry, string functionName, string description, Func<TContext, string> resultProvider)
    {
        var flagTool = new ToolBuilder(functionName)
            .WithDescription(description)
            .Build();

        var handler = new ReviewFlagToolHandler<TContext>(flagTool, resultProvider);

        registry.RegisterTool(flagTool, handler);
    }
    
    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<TContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(Success(resultProvider(runner.ExecutionContext)));
        }
        catch (InvalidOperationException ex)
        {
            // Contradictory flag (e.g. reject then approve, or approve then reject)
            return Task.FromResult(Error(ex.Message));
        }
    }
}