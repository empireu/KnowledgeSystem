using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Retrieval;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Agent.Tools;

public sealed partial class FastContextToolHandler(
    AgentTool tool,
    StringArgument queryArgument,
    StringArgument pathFilterArgument,
    IServiceProvider serviceProvider,
    FastContextToolConfig config
) : ToolHandler<ConversationalContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<ConversationalContext> registry, IServiceProvider serviceProvider, FastContextToolConfig config)
    {
        var searchTool = new ToolBuilder("fast_context")
            .WithDescription("Searches the knowledge base for all information related to the topic. Provide a rich sentence to maximize recall! Optionally filter by file path.")
            .WithRequiredStringArgument("query", "A rich, descriptive sentence describing the information needed. More detail improves recall.", out var queryArg)
            .WithStringArgument("pathFilter", "Optional case-insensitive regex that filters which file paths to include (e.g., 'docs' or '\\.md$'). Only use when the query targets specific files or directories.", out var filterArg)
            .Build();
        
        var handler = ActivatorUtilities.CreateInstance<FastContextToolHandler>(
            serviceProvider,
            searchTool,
            queryArg,
            filterArg,
            config
        );
        
        registry.RegisterTool(searchTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<ConversationalContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var query = queryArgument.GetValue(args);
        var pathFilter = pathFilterArgument.GetValueOrNull(args);
        
        if (string.IsNullOrWhiteSpace(query))
        {
            return Error("fast_context: Empty query argument!");
        }

        FastContextRetrieval retrieval;
        
        try
        {
            retrieval = ActivatorUtilities.CreateInstance<FastContextRetrieval>(serviceProvider, new FastContextRetrieval.Description
            {
                Query = query,
                BootstrapCount = config.BootstrapCount,
                Parameter = config.Parameter,
                Bm25Results = config.Bm25Results,
                FilePathPattern = pathFilter
            });
        }
        catch (ArgumentException ex)
        {
            return Error($"fast_context: Failed to construct regex: {ex.Message}");
        }

        await retrieval.PrepareForRun(cancellationToken);

        var turns = 0;
        int chars;
        do
        {
            chars = retrieval.Step(config.BatchSize);
            ++turns;
        } while (!retrieval.IsExhausted && chars < config.MaxDirectCharCount && turns < config.MaxTurns);

        retrieval.FuseScoresAndFinish();
       
        var sb = new StringBuilder();
        
        var gaps = retrieval.ExtractGapTokens();
      
        if (gaps.Count > 0)
        {
            gaps.Sort((a, b) => a.PValue.CompareTo(b.PValue));

            sb.AppendLine("fast_context: Warning! Some terms are under-represented in the search results:");
            for (var index = 0; index < gaps.Count; index++)
            {
                var gapToken = gaps[index];

                sb.AppendLine($"{index}. \"{gapToken.Token}\" - appears {gapToken.InResults} times, exists {gapToken.InCorpus} times across all documents");
            }

            sb.AppendLine("If those are important tokens, consider doing a search with each token itself only.");
        }
        
        var results = retrieval.ReferencedDocuments.Values.ToList();
        results.Sort((a, b) => a.AverageScore.CompareTo(b.AverageScore));
        
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
