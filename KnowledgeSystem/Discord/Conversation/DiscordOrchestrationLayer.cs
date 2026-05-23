using KnowledgeSystem.Agent;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Discord.Integration;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace KnowledgeSystem.Discord.Conversation;

/// <summary>
///     Holds the linked elements of the pipeline for handling a single user request.
/// </summary>
public sealed class DiscordOrchestrationLayer
{
    public required ConversationalAgent RootAgent { get; init; }
    public required AgentRunner<ConversationalContext> RootRunner { get; init; }
    public required DiscordMessageIntegration DiscordIntegration { get; init; }
}