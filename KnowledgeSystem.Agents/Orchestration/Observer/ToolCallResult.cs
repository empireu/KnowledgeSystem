using KnowledgeSystem.Agents.Tools;

namespace KnowledgeSystem.Agents.Orchestration.Observer;

/// <summary>
///     Observer information for a tool call result within a round (comes after <see cref="ToolCallInfo"/>).
/// </summary>
public sealed class ToolCallResult
{
    /// <summary>
    ///     The index in the original <see cref="ToolCallInfo"/> array.
    /// </summary>
    public required int IndexInCollection { get; init; }
 
    /// <summary>
    ///     The same tool as the one passed in the initial array.
    /// </summary>
    public required AgentTool Tool { get; init; }
    
    /// <summary>
    ///     The raw content returned by the tool.
    /// </summary>
    public required string Content { get; init; }
}