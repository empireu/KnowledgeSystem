using OpenAI.Chat;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace KnowledgeSystem.Agents.Tools;

/// <summary>
///     Wraps an OpenAI tool.
/// </summary>
public sealed class ToolDefinition
{
    public required string ToolId { get; init; }
    public required string? Description { get; init; }
    public required IReadOnlyList<ToolArgument> Arguments { get; init; }
    public required IReadOnlyList<ToolArgument> RequiredArguments { get; init; }
    public required ChatTool Tool { get; init; }
}