using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Wiki.Memory;

public sealed class DeleteMemoryToolHandler(
    AgentTool tool,
    IntegerArgument memoryIdArgument,
    MemoryStoreService memoryStore
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider)
    {
        var deleteTool = new ToolBuilder("delete_memory")
            .WithDescription("Deletes a memory by its ID. Use this to remove outdated, superseded, or duplicate memories after you have created a consolidated replacement.")
            .WithRequiredIntegerArgument("memory_id", "The numeric ID of the memory to delete.", out var idArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<DeleteMemoryToolHandler>(
            serviceProvider,
            deleteTool,
            idArg
        );

        registry.RegisterTool(deleteTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(
        AgentRunner<BasicContext> runner,
        ArgumentExtractionResult args,
        CancellationToken cancellationToken)
    {
        var id = memoryIdArgument.GetValue(args);

        var deleted = await memoryStore.DeleteMemory(id, cancellationToken);

        return deleted
            ? Success($"Memory `{id}` deleted.")
            : Error($"No memory found with ID `{id}`.");
    }
}