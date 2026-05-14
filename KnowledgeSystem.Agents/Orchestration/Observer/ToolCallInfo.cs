using KnowledgeSystem.Agents.Tools;

namespace KnowledgeSystem.Agents.Orchestration.Observer;

/// <summary>
///     Observer information for a tool call within a round.
/// </summary>
public readonly struct ToolCallInfo
{
    /// <summary>
    ///     The executed tool.
    /// </summary>
    public required AgentTool Tool { get; init; }
    
    /// <summary>
    ///     The extracted arguments.
    /// </summary>
    public required ArgumentExtractionResult Args { get; init; }
}