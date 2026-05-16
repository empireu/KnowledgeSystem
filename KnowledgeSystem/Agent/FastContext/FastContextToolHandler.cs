using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Retrieval;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Agent.FastContext;

public sealed partial class FastContextToolHandler(
    AgentTool tool,
    StringArgument queryArgument,
    IServiceProvider serviceProvider,
    FastContextToolConfig config
) : ToolHandler<ConversationalContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<ConversationalContext> registry, IServiceProvider serviceProvider, FastContextToolConfig config)
    {
        var searchTool = new ToolBuilder("fast_context")
            .WithDescription("Searches the knowledge base for all information related to the topic. Provide a rich sentence to maximize recall!")
            .WithRequiredStringArgument("query", "The search query.", out var queryArg)
            .Build();
        
        var handler = new FastContextToolHandler(searchTool, queryArg, serviceProvider, config);
        
        registry.RegisterTool(searchTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<ConversationalContext> runner, ArgumentExtractionResult args, ConversationalContext runContext, CancellationToken cancellationToken)
    {
        var query = queryArgument.GetValue(args);

        if (string.IsNullOrWhiteSpace(query))
        {
            return Error("fast_context: Empty query argument!");
        }

        var retrieval = ActivatorUtilities.CreateInstance<FastContextRetrieval>(serviceProvider, new FastContextRetrieval.Description
        {
            Query = query,
            BootstrapCount = config.BootstrapCount,
            Parameter = config.Parameter
        });

        await retrieval.PrepareForRun(cancellationToken);

        var turns = 0;
        int chars;
        do
        {
            chars = retrieval.Step(config.BatchSize);
            ++turns;
        } while (!retrieval.IsExhausted && chars < config.MaxDirectCharCount && turns < config.MaxTurns);

        var results = retrieval.ReferencedDocuments.Values.ToList();
        results.Sort((a, b) => a.AverageScore.CompareTo(b.AverageScore));

        var sb = new StringBuilder();

        if (chars > config.MaxDirectCharCount)
        {
            CompactExtraction(sb, query, results);
        }
        else
        {
            DirectExtraction(sb, results);
        }
       

        return Success(sb.ToString());
    }

    /// <summary>
    ///     Formats the content directly in raw form.
    /// </summary>
    private static void DirectExtraction(StringBuilder sb, List<FastContextRetrieval.ReferencedDocument> results)
    {
        sb.AppendLine($"# fast_context: {results.Count} documents found. Extracted {results.Sum(x => x.BoundingTreesSorted.Count)} sections:");
        foreach (var referencedDocument in results)
        {
            sb.AppendLine($"# Document: {referencedDocument.Document.Path} - {referencedDocument.BoundingTreesSorted.Count} sections");

            foreach (var boundingTree in referencedDocument.BoundingTreesSorted)
            {
                var start = boundingTree.Root.StartOffset;
                var length = boundingTree.Root.EndOffset - start;
                sb.AppendLine(referencedDocument.Document.Content.Substring(start, length));
                sb.AppendLine();
            }
        }
    }
}
