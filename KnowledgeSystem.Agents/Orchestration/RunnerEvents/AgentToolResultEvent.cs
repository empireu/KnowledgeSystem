using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Events.Api;

namespace KnowledgeSystem.Agents.Orchestration.RunnerEvents;

/// <summary>
///     Dispatched when the execution of a tool finishes. Always comes after the initial <see cref="AgentToolCallsEvent"/> passes the tool calls.
/// </summary>
/// <param name="IndexInCollection">The index in <see cref="AgentToolCallsEvent.Calls"/>.</param>
public record AgentToolResultEvent(int IndexInCollection, AgentTool Tool, ToolExecutionResult Result) : IEvent;