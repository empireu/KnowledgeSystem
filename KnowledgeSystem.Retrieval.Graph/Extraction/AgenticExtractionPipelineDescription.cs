using KnowledgeSystem.Events.Api;
using KnowledgeSystem.Events.Implementation;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Retrieval.Graph.Extraction;

public sealed class AgenticExtractionPipelineDescription
{
    public required IChatClient ChatClient { get; init; }

    public IEventManager EventManager { get; init; } = NullEventManager.Instance;

    public required string SystemPrompt { get; init; }

    public int MaxTurns { get; init; } = 30;
}