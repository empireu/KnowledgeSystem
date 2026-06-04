using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Retrieval.Api.Graph;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Retrieval.Graph.Extraction;

/// <summary>
///     Execution context for the extraction agent.
///     Accumulates entities and claims as the agent calls tools.
/// </summary>
public class ExtractionContext : AgentExecutionContext
{
    public AgentContext Timeline { get; } = new();

    public override IReadOnlyList<ChatMessage> ChatMessages => Timeline.ChatMessages;

    public override void InsertAssistantCompletion(ChatResponse response)
    {
        Timeline.InsertAssistant(response);
    }

    public override void InsertToolResult(string toolCallId, string output)
    {
        var contents = new List<AIContent>
        {
            new FunctionResultContent(toolCallId, output)
        };

        Timeline.InsertChat(new ChatMessage(ChatRole.Tool, contents));
    }

    /// <summary>
    ///     The source text being extracted from.
    /// </summary>
    public required string SourceContent { get; init; }

    /// <summary>
    ///     Entities recorded so far, keyed by primary name (case-insensitive).
    /// </summary>
    public Dictionary<string, RawExtractedEntity> RecordedEntities { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Claims recorded so far.
    /// </summary>
    public List<RawExtractedClaim> RecordedClaims { get; } = [];

    /// <summary>
    ///     Whether the agent has signaled completion.
    /// </summary>
    public bool MarkedFinished { get; set; }
}
