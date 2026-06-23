using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.CodeMemory.Memory;

/// <summary>
///     Tool for the memory synthesis agent to persist a distilled memory.
/// </summary>
public sealed class CreateMemoryToolHandler<TContext>(
    string @namespace,
    AgentTool tool,
    StringArgument summaryArgument,
    StringArgument contentArgument,
    MemoryStoreService memoryStoreService
) : ToolHandler<TContext>.Plain(tool) where TContext : BasicContext
{
    public static void Register<TContext>(string @namespace, AgentToolRegistry<TContext> registry, IServiceProvider serviceProvider) where TContext : BasicContext
    {
        var memoryTool = new ToolBuilder("create_memory")
            .WithDescription("Creates a new memory from the conversation. Call this for each distinct, factual conclusion you can extract. The summary is a one-line description used for future retrieval; the content is the full distilled conclusion.")
            .WithRequiredStringArgument("summary", "One-line summary used for semantic retrieval (max ~100 chars).", out var summaryArg)
            .WithRequiredStringArgument("content", "Full distilled conclusion with all relevant details.", out var contentArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<CreateMemoryToolHandler<TContext>>(
            serviceProvider,
            @namespace,
            memoryTool,
            summaryArg,
            contentArg
        );

        registry.RegisterTool(memoryTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(
        AgentRunner<TContext> runner,
        ArgumentExtractionResult args,
        CancellationToken cancellationToken)
    {
        var summary = summaryArgument.GetValue(args);
        var content = contentArgument.GetValue(args);

        if (string.IsNullOrWhiteSpace(summary))
        {
            return Error("Memory summary must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Error("Memory content must not be empty.");
        }

        if (summary.Length > 512)
        {
            return Error($"Memory summary too long ({summary.Length} chars, max 512).");
        }

        try
        {
            var id = await memoryStoreService.CreateMemory(
                @namespace,
                summary,
                content,
                DateTime.UtcNow, // Close enough
                cancellationToken
            );
            
            return Success($"Memory created with ID `{id}`.");
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(
                Tool,
                false,
                null,
                "System error. You should abort your task now.",
                ex
            );
        }
    }
}
