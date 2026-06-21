using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using KnowledgeSystem.Plugins.Wiki.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Wiki.Wiki;

public sealed class FetchMemoryToolHandler(
    AgentTool tool,
    IntegerArgument memoryIdArgument,
    MemoryStoreService memoryStore
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider)
    {
        var fetchTool = new ToolBuilder("fetch_memory")
            .WithDescription("Fetches the full content of a previously stored memory by its ID. Use this when a 'potentially relevant memory' summary looks useful and you want the full details.")
            .WithRequiredIntegerArgument("memory_id", "The numeric ID of the memory to fetch.", out var idArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<FetchMemoryToolHandler>(
            serviceProvider,
            fetchTool,
            idArg
        );

        registry.RegisterTool(fetchTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(
        AgentRunner<BasicContext> runner,
        ArgumentExtractionResult args,
        CancellationToken cancellationToken)
    {
        var id = memoryIdArgument.GetValue(args);
        
        var memory = await memoryStore.GetMemoryAsync(id, cancellationToken);

        return memory == null 
            ? Error($"No memory found with ID `{id}`.")
            : Success(memory.Content);
    }
}
