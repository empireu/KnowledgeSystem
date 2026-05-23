using KnowledgeSystem.Events.Api;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Agents.Orchestration.RunnerEvents;

/// <summary>
///     Dispatched when the agent runs tools in a turn.
/// </summary>
public record AgentToolCallsEvent(ChatResponse Response, ToolCallInfo[] Calls) : IEvent;