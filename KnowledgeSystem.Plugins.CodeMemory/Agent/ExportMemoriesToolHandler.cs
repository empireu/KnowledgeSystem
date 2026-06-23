using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;

namespace KnowledgeSystem.Plugins.CodeMemory.Agent;

public class ExportMemoriesToolHandler(AgentTool tool, ArrayArgument idArrayArgument) : ToolHandler<RecallContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<RecallContext> registry)
    {
        var recallTool = new ToolBuilder("export_memories")
            .WithDescription("Marks the supplied array of memory IDs to be kept.")
            .WithRequiredArrayArgument("memory_id_array", "An array of memory ID integers.", out var idArrayArg)
            .Build();
        
        var handler = new ExportMemoriesToolHandler(recallTool, idArrayArg);

        registry.RegisterTool(recallTool, handler);
    }
    
    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<RecallContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var idStringArray = idArrayArgument.GetValue(args);

        if (idStringArray.Length == 0)
        {
            return Task.FromResult(Success("No memories marked. Nothing to export."));
        }
        
        var success = new List<int>(idStringArray.Length);
        var fail = new List<string>(0);
        foreach (var str in idStringArray)
        {
            if (int.TryParse(str, out var id))
            {
                if (!runner.ExecutionContext.MarkedMemories.Contains(id))
                {
                    runner.ExecutionContext.MarkedMemories.Add(id);
                }
                
                success.Add(id);
            }
            else
            {
                fail.Add(str);
            }
        }

        var sb = new StringBuilder();

        if (success.Count > 0)
        {
            sb.AppendLine($"Successfully added {string.Join(", ", success)}");
        }

        if (fail.Count > 0)
        {
            sb.AppendLine($"Failed to parse IDs {string.Join(", ", fail.Select(x => $"\"{x}\""))}");
        }

        return Task.FromResult(Success(sb.ToString()));
    }
}