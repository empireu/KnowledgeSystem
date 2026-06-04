using KnowledgeSystem.Agents.Orchestration;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Retrieval.Graph.Extraction;

/// <summary>
///     An extraction agent that uses tool calls to record entities and claims.
/// </summary>
public sealed class ExtractionAgent : Agent<ExtractionContext>
{
    public ExtractionAgent() : base("extraction")
    {
        RecordEntityToolHandler.Register(ToolRegistry);
        RecordClaimToolHandler.Register(ToolRegistry);
        FinishExtractionToolHandler.Register(ToolRegistry);
    }

    public override Task<AgentCallbackResult> HandleCompletion(AgentRunner<ExtractionContext> runner, ChatResponse response)
    {
        runner.ExecutionContext.Timeline.InsertSystem("You need to call one of the tools to proceed. If the investigation is finished, call the finish_extraction tool; otherwise, continue with recording the remaining entities and claims.");
        return Task.FromResult(AgentCallbackResult.Continue);
    }

    public override Task<AgentCallbackResult> HandleToolFinish(AgentRunner<ExtractionContext> runner)
    {
        if (runner.ExecutionContext.MarkedFinished)
        {
            return Task.FromResult(AgentCallbackResult.Break);
        }

        // Otherwise, continue the loop so the agent can make more tool calls:
        return Task.FromResult(AgentCallbackResult.Continue);
    }
}
