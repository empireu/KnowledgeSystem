using System.ComponentModel.DataAnnotations.Schema;
using KnowledgeSystem.Agents.Tools;

namespace KnowledgeSystem.Agents.Orchestration.Tools;

/// <summary>
///     Tool set with a registered handler for each tool.
/// </summary>
public sealed class AgentToolRegistry<TContext> where TContext : AgentExecutionContext
{
    private readonly Dictionary<AgentTool, ToolHandler<TContext>> _handlers = [];
    private readonly ToolSet _toolSet = new();
    
    public IReadOnlyDictionary<AgentTool, ToolHandler<TContext>> Handlers => _handlers;
    public IReadOnlyToolSet ToolSet => _toolSet;
    
    /// <summary>
    ///     Registers a tool and handler.
    /// </summary>
    /// <param name="tool"></param>
    /// <param name="handler"></param>
    public void RegisterTool(AgentTool tool, ToolHandler<TContext> handler)
    {
        _toolSet.AddTool(tool);
        _handlers.Add(tool, handler);
    }
}