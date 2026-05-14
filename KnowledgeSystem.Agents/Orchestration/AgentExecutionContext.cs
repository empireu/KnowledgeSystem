using OpenAI.Chat;

namespace KnowledgeSystem.Agents.Orchestration;

/// <summary>
///     Represents the state of a running agent.
/// </summary>
public abstract class AgentExecutionContext
{
    /// <summary>
    ///     The parent context. Will be null for the main orchestration agent.
    /// </summary>
    public AgentExecutionContext? ParentExecutionContext { get; init; }

    /// <summary>
    ///     The chat messages to be sent to the LLM.
    /// </summary>
    public abstract IReadOnlyList<ChatMessage> ChatMessages { get; }

    /// <summary>
    ///     Inserts an assistant completion (with tool calls) into the context.
    /// </summary>
    public abstract void InsertAssistantCompletion(ChatCompletion completion);

    /// <summary>
    ///     Inserts a tool result into the context.
    /// </summary>
    public abstract void InsertToolResult(string toolCallId, string output);
}