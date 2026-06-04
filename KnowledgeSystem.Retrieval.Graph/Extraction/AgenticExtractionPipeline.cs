using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Retrieval.Api.Graph;

namespace KnowledgeSystem.Retrieval.Graph.Extraction;

/// <summary>
///     An agentic extraction pipeline that uses tool-calling to extract entities and claims.
///     The idea behind implementing it as an agent is reducing failures.
///     Trying to get the LLM to one-shot the entities and claims with valid evidence seems to have an incredibly low success rate, even with good models.
///     The tool-calling loop will report errors and the LLM can fix them.
/// </summary>
public sealed class AgenticExtractionPipeline(AgenticExtractionPipelineDescription description) : IFeatureExtractionPipeline
{
    public async Task<RawProcessedIngestionChunk> IngestAsync(IngestionChunkSource source, CancellationToken cancellationToken = default)
    {
        var context = new ExtractionContext
        {
            SourceContent = source.Content
        };

        context.Timeline.InsertSystem(description.SystemPrompt);
        context.Timeline.InsertSystem(source.Content);

        var agent = new ExtractionAgent();

        var runner = new AgentRunner<ExtractionContext>(
            client: description.ChatClient,
            agent: agent,
            parent: null,
            context: context,
            eventManager: description.EventManager,
            cancellationToken: cancellationToken,
            completionFactory: null
        );

        for (var turn = 0; !runner.IsFinished; cancellationToken.ThrowIfCancellationRequested())
        {
            if (turn > description.MaxTurns)
            {
                throw new Exception("Agent exceeded turn limit");
            }
         
            var status = await runner.ExecuteTurn();

            switch (status)
            {
                case AgentRunner.TurnStatus.ToolCallsReceived:
                case AgentRunner.TurnStatus.CompletedSuccessfully:
                case AgentRunner.TurnStatus.CompletedWithError:
                case AgentRunner.TurnStatus.CompletionHandled:
                    turn++;
                    break;
            }
        }

        var entities = context.RecordedEntities.Values.ToArray();
        var claims = context.RecordedClaims.ToArray();

        return new RawProcessedIngestionChunk(source.Content)
        {
            Entities = entities,
            Claims = claims
        };
    }
}
