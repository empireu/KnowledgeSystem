using System.ComponentModel;
using System.Text;
using KnowledgeSystem.Plugins.CodeMemory.Memory;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// ReSharper disable UnusedMember.Global

namespace KnowledgeSystem.Plugins.CodeMemory;

[McpServerToolType]
public sealed class CodeMemoryTools(
    ILogger<CodeMemoryTools> logger,
    MemoryExtractionService extractionService,
    MemoryStoreService storeService,
    RecallSystem recallSystem
)
{
    [McpServerTool, Description(
        "Stores a new discovery, procedure, quirk, or lesson learned during development. " +
        "Fire-and-forget — the statement is queued for background processing, which will " +
        "search for duplicates, consolidate, and persist it. Create memories " +
        "for non-obvious things that would be hard to rediscover.")]
    public async Task<string> ProduceStatement(
        [Description(
            "The project namespace to store this under (e.g. 'KnowledgeSystem' or 'MyGame')." +
            "Must be exact; it is case-sensitive.")] string projectNamespace,
        [Description(
            "The full description of what was discovered — procedures, quirks, gotchas, " +
            "or a design decision with rationale, and any other extra rules set by the USER. " +
            "This statement will be processed by an agent, so you can address the agent in this text " +
            "if you need to make sure important specific points are persisted.")] string statement,
        CancellationToken ct)
    {
        await extractionService.EnqueueStatementAsync(
            new PendingStatement(projectNamespace, statement, DateTime.UtcNow),
            ct
        );

        return $"Statement queued for processing under namespace '{projectNamespace}'.";
    }

    [McpServerTool, Description(
        "Searches for relevant memories and returns a compact list of memory IDs with their summaries. " +
        "Use this first to find what's available — then call fetch_memory with the IDs you want to read in full. " +
        "Uses hybrid search (semantic + keyword) and returns the top matches. " +
        "Use dense language (e.g 'Entity implementation guide') that reads as a query.")]
    public async Task<string> SearchMemories(
        [Description("The project namespace to search in (e.g. 'KnowledgeSystem' or 'MyGame').")] string @namespace,
        [Description("What you need to know — describe the feature, context, or question specifically.")] string query,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
        {
            logger.LogWarning("Tried to search for empty namespace");
            return "Error: Cannot have empty namespace!";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            logger.LogWarning("Tried to search with empty query");
            return "Error: Cannot have empty query!";
        }
        
        var results = await storeService.SearchAsync(@namespace, query, ct);

        if (results.Length == 0)
        {
            return $"No relevant memories found for '{query}' in namespace '{@namespace}'.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Length} relevant memories:\n");

        foreach (var m in results)
        {
            sb.AppendLine($"  Memory ID {m.Id}: {m.Summary}");
        }

        sb.AppendLine("\nUse fetch_memory with the desired ID to read the full content.");

        return sb.ToString();
    }

    [McpServerTool, Description(
        "Retrieves the full content of a stored memory by its ID. " +
        "Call this after retrieve_knowledge to read the complete details " +
        "of a memory whose summary looked relevant.")]
    public async Task<string> FetchMemory(
        [Description("The numeric ID of the memory to fetch.")] int memoryId,
        CancellationToken ct)
    {
        var memory = await storeService.GetMemoryAsync(memoryId, ct);

        if (memory == null)
        {
            return $"No memory found with ID {memoryId}.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Memory {memory.Id} (from {memory.Namespace})");
        sb.AppendLine(memory.Content);
        return sb.ToString();
    }

    [McpServerTool, Description(
         "Invokes an agent that searches for relevant memories and returns all matching results. " +
         "Use this to get all the procedures, APIs, etc. that you need for your task. " +
         "The agent will understand natural-language queries, so decribe everything you need in one call. ")]
    public async Task<string> RecallMemories(
        [Description("The project namespace to search in (e.g. 'KnowledgeSystem' or 'MyGame').")] string @namespace,
        [Description("What you need to know — describe the features, procedures, APIs, and conventions you need to recall.")] string query,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
        {
            logger.LogWarning("Tried to recall for empty namespace");
            return "Error: Cannot have empty namespace!";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            logger.LogWarning("Tried to recall with empty query");
            return "Error: Cannot have empty query!";
        }

        return await recallSystem.RecallAsync(@namespace, query, ct);
    }
}
