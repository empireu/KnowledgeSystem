using KnowledgeSystem.Agents.Tools;

namespace KnowledgeSystem.Agents.Orchestration;

/// <summary>
///     Information for a tool call within a round.
/// </summary>
public readonly struct ToolCallInfo
{
    /// <summary>
    ///     If true, the call at this index is valid.
    /// </summary>
    public required bool IsValid { get; init; }
    
    /// <summary>
    ///     The executed tool.
    /// </summary>
    public required AgentTool Tool { get; init; }
    
    /// <summary>
    ///     The extracted arguments.
    /// </summary>
    public required ArgumentExtractionResult Args { get; init; }
}