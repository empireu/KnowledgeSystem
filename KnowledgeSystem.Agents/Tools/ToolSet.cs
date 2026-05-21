using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Agents.Tools;

public interface IReadOnlyToolSet
{
    /// <summary>
    ///     Adds the tools to the completion options.
    /// </summary>
    /// <param name="options"></param>
    void AddToOptions(ChatOptions options);
    
    /// <summary>
    ///     Tools by their tool ID.
    /// </summary>
    public IReadOnlyDictionary<string, AgentTool> Tools { get; }

    /// <summary>
    ///     Tries to match a tool from the model's response.
    /// </summary>
    /// <param name="toolCall">The raw tool call content from the response.</param>
    /// <param name="tool">If true, the tool that was matched.</param>
    /// <returns>True if a tool was matched. Otherwise, false.</returns>
    public bool TryMatchTool(FunctionCallContent toolCall, [NotNullWhen(true)] out AgentTool? tool);
}

public sealed class ToolSet : IReadOnlyToolSet
{
    private readonly Dictionary<string, AgentTool> _tools = [];

    public IReadOnlyDictionary<string, AgentTool> Tools => _tools;

    public void AddToOptions(ChatOptions options)
    {
        options.Tools ??= [];
        foreach (var agentTool in _tools.Values)
        {
            options.Tools.Add(agentTool.CreateSchemaFunction());
        }
    }
    
    public bool TryMatchTool(FunctionCallContent toolCall, [NotNullWhen(true)] out AgentTool? tool)
    {
        return _tools.TryGetValue(toolCall.Name, out tool);
    }
    
    /// <summary>
    ///     Adds a tool to the set.
    /// </summary>
    public void AddTool(AgentTool agentTool)
    {
        if (!_tools.TryAdd(agentTool.ToolId, agentTool))
        {
            throw new InvalidOperationException($"Duplicate tool definition {agentTool.ToolId}");
        }
    }
}