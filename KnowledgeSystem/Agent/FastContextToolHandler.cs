using System.Text;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Agent;

public sealed class FastContextToolHandler(AgentTool tool, StringArgument queryArgument, IServiceProvider serviceProvider) : ToolHandler<SimpleChatContext>(tool)
{
    public static void Register(AgentToolRegistry<SimpleChatContext> registry, IServiceProvider serviceProvider)
    {
        var searchTool = new ToolBuilder("fast_context")
            .WithDescription("Searches the knowledge base for all information related to the topic. Provide a rich sentence to maximize recall!")
            .WithRequiredStringArgument("query", "The search query.", out var queryArg)
            .Build();
        
        var handler = new FastContextToolHandler(searchTool, queryArg, serviceProvider);
        
        registry.RegisterTool(searchTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ArgumentExtractionResult args, SimpleChatContext runContext, CancellationToken cancellationToken)
    {
        var query = queryArgument.GetValue(args);

        var retrieval = ActivatorUtilities.CreateInstance<FastContextRetrieval>(serviceProvider, new FastContextRetrieval.Description
        {
            Query = query
        });

        await retrieval.PrepareForRun(cancellationToken);

        do
        {
            retrieval.Step(10);
        } while (!retrieval.IsExhausted);

        var results = retrieval.ReferencedDocuments.Values.ToList();
        results.Sort((a, b) => a.AverageScore.CompareTo(b.AverageScore));

        var sb = new StringBuilder();
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

        return new ToolExecutionResult(Tool, ToolExecutionResult.Status.Success, sb.ToString(), null, null);
    }
}
