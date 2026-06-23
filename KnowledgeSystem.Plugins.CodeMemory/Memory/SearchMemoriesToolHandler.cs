using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.CodeMemory.Memory;

public sealed class SearchMemoriesToolHandler<TContext>(
    string @namespace,
    AgentTool tool,
    StringArgument queryArgument,
    MemoryStoreService memoryStore
) : ToolHandler<TContext>.Plain(tool) where TContext : BasicContext
{
    public static void Register(string @namespace, AgentToolRegistry<TContext> registry, IServiceProvider serviceProvider)
    {
        var searchTool = new ToolBuilder("search_memories")
            .WithDescription("Searches existing memories for the given query. Returns the top matching memories with their IDs and summaries. Use this before creating new memories to avoid duplicates.")
            .WithRequiredStringArgument("query", "The search query to find relevant memories.", out var queryArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<SearchMemoriesToolHandler<TContext>>(
            serviceProvider,
            @namespace,
            searchTool,
            queryArg
        );

        registry.RegisterTool(searchTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(
        AgentRunner<TContext> runner,
        ArgumentExtractionResult args,
        CancellationToken cancellationToken)
    {
        var query = queryArgument.GetValue(args);

        if (string.IsNullOrWhiteSpace(query))
        {
            return Error("Search query must not be empty.");
        }

        var results = await memoryStore.SearchAsync(
            @namespace, 
            query,
            cancellationToken
        );

        if (results.Length == 0)
        {
            return Success("No matching memories found.");
        }

        var sb = new StringBuilder();

        foreach (var memoryRecord in results)
        {
            sb.AppendLine($"{memoryRecord.Id}. {memoryRecord.Summary}");
        }
        
        return Success(sb.ToString());
    }
}