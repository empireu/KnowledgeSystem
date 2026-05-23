using KnowledgeSystem.Events.Api;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Agents.Orchestration.RunnerEvents;

/// <summary>
///     Dispatched when the agent outputs a message that doesn't contain any tool calls.
/// </summary>
public record AgentMessageEvent(ChatResponse Response) : IEvent;