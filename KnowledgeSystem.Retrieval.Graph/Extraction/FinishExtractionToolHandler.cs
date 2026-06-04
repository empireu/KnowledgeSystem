using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;

namespace KnowledgeSystem.Retrieval.Graph.Extraction;

public sealed class FinishExtractionToolHandler(AgentTool tool) : ToolHandler<ExtractionContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<ExtractionContext> registry)
    {
        var finishTool = new ToolBuilder("finish_extraction")
            .WithDescription("Call when extraction is complete.")
            .Build();

        var handler = new FinishExtractionToolHandler(finishTool);
       
        registry.RegisterTool(finishTool, handler);
    }

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<ExtractionContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        runner.ExecutionContext.MarkedFinished = true;
        var entityCount = runner.ExecutionContext.RecordedEntities.Count;
        var claimCount = runner.ExecutionContext.RecordedClaims.Count;
        return Task.FromResult(Success($"Done: {entityCount} entities, {claimCount} claims."));
    }
}
