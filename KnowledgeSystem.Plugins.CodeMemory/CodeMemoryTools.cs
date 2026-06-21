using System.ComponentModel;
using System.Text;
using KnowledgeSystem.Plugins.CodeMemory.Memory;
using ModelContextProtocol.Server;

// ReSharper disable UnusedMember.Global

namespace KnowledgeSystem.Plugins.CodeMemory;

[McpServerToolType]
public sealed class CodeMemoryTools(
    MemoryExtractionService extractionService,
    MemoryStoreService storeService)
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
        "Retrieves relevant knowledge from stored project memories. " +
        "Searches across the specified project's memories using semantic and keyword search, " +
        "then takes the results that are relevant to the query, based on the judgement of a small language model. " +
        "Call this before implementing a feature to check for relevant procedures, " +
        "quirks, and design decisions learned in previous sessions. Use dense language (e.g 'Entity implementation guide') that reads as a query.")]
    public async Task<string> RetrieveKnowledge(
        [Description("The project namespace to search in (e.g. 'KnowledgeSystem' or 'MyGame').")] string @namespace,
        [Description("What you need to know — describe the feature, context, or question specifically.")] string query,
        CancellationToken ct)
    {
        var results = await storeService.SearchAsync(@namespace, query, ct);

        if (results.Length == 0)
        {
            return $"No relevant memories found for '{query}' in namespace '{@namespace}'.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Length} relevant memories:\n");

        foreach (var m in results)
        {
            sb.AppendLine($"# Memory ID {m.Id} ({m.Summary})");
            sb.AppendLine($"{m.Content}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
