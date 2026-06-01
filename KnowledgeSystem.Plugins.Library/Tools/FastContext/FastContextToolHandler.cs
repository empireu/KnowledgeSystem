using System.Diagnostics;
using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Api;
using KnowledgeSystem.EmdParser.MarkdownTree;
using KnowledgeSystem.Lexical;
using KnowledgeSystem.Plugins.Library.Tools.Markers;
using KnowledgeSystem.Retrieval.Api.Store;
using KnowledgeSystems.Extensions;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Plugins.Library.Tools.FastContext;

public sealed class FastContextToolHandler(
    AgentTool tool,
    StringArgument queryArgument,
    IReadOnlyMarkdownDocumentStore store,
    FastContextToolConfig config
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(
        AgentToolRegistry<BasicContext> registry,
        IReadOnlyMarkdownDocumentStore store,
        IServiceProvider serviceProvider,
        FastContextToolConfig config)
    {
        var searchTool = new ToolBuilder("fast_context")
            .WithDescription("Searches the knowledge base for all information related to the topic. Provide a rich sentence to maximize recall!")
            .WithRequiredStringArgument("query", "A rich, descriptive sentence describing the information needed. More detail improves recall.", out var queryArg)
            .Build();
        
        var handler = ActivatorUtilities.CreateInstance<FastContextToolHandler>(
            serviceProvider,
            searchTool,
            queryArg,
            store,
            config
        );
        
        registry.RegisterTool(searchTool, handler);
    }
    
    
    private readonly IMarkdownVectorSearchCapability _vectorCapability = store
        .GetCapability<IMarkdownVectorSearchCapability>(IMarkdownVectorSearchCapability.CapabilityType);
    
    private readonly ILexicalMarkdownSearchCapability _lexicalCapability = store
        .GetCapability<ILexicalMarkdownSearchCapability>(ILexicalMarkdownSearchCapability.CapabilityType);
    
    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var query = queryArgument.GetValue(args);
        
        if (string.IsNullOrWhiteSpace(query))
        {
            return Error("fast_context: Empty query argument!");
        }

        using var activity = KnowledgeSystemTelemetry.AgentTools.StartInternalActivity("FastContext");
        activity?.SetTag("query", query);

        var retrieval = new FastContextRetrievalPipeline(store, _vectorCapability, _lexicalCapability, new FastContextRetrievalPipeline.Description
        {
            Query = query,
            BootstrapCount = config.BootstrapCount,
            Parameter = config.Parameter,
            Bm25Results = config.Bm25Results,
            MaxResults = config.MaxResults
        });

        using (KnowledgeSystemTelemetry.AgentTools.StartInternalActivity("PrepareForRun"))
        {
            await retrieval.PrepareForRun(cancellationToken);
        }

        using (var runActivity = KnowledgeSystemTelemetry.AgentTools.StartInternalActivity("Retrieval"))
        {
            var chars = retrieval.Run();
            runActivity?.SetTag("chars", chars);
        }

        using (KnowledgeSystemTelemetry.AgentTools.StartInternalActivity("Evaluate"))
        {
            retrieval.FuseScoresAndFinish();
        }

        activity?.SetTag("document_count", retrieval.ReferencedDocuments.Count);
        activity?.SetTag("reference_count", retrieval.ReferencedDocuments.Values.Sum(x => x.References.Count));
        
        var sb = new StringBuilder();
        
        var gaps = retrieval.ExtractGapTokens(config.SignificanceLevel);
      
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
        results.Sort((a, b) => b.AverageScore.CompareTo(a.AverageScore));
        
        var ranges = CompactExtraction(sb, query, results);
        var result = sb.ToString();
        
        activity?.SetTag("result_size", result.Length);
        activity?.SetStatus(ActivityStatusCode.Ok);
        
        runner.ExecutionContext.Timeline.InsertElement(new FastContextMarker
        {
            Output = result,
            Ranges = ranges
        });
        
        return Success(result);
    }

    /// <summary>
    ///     Pulls and formats references so the content can be inspected with other tools.
    ///     Returns structured <see cref="ContentRange"/> list for deduplication in the peer review sub-agent.
    /// </summary>
    private List<ContentRange> CompactExtraction(StringBuilder sb, string query, List<FastContextRetrievalPipeline.ReferencedDocument> results)
    {
        var totalTrees = results.Sum(r => r.BoundingTreesSorted.Count);
        if (results.Count > 3 || totalTrees > 10)
        {
            sb.AppendLine("fast_context: Too much content found. Here are the paths, offsets `a,b`, sections written as `@XXX` (if they exist), and snippets with their offsets `'...and the query is...':x,y` of the most relevant results for targeted inspection:");
        }
        else
        {
            sb.AppendLine("fast_context: Here are the paths, offsets `a,b`, sections written as `@XXX` (if they exist), and snippets with their offsets `'...and the query is...':x,y` of the most relevant results:");
        }

        var tokens = Tokenizer.TokenizeQuery(query, true);
        var ranges = new List<ContentRange>();

        string? currentDir = null;
        foreach (var referencedDocument in results)
        {
            var path = referencedDocument.Document.Path;
            var lastSlash = path.LastIndexOf('/');
            var dir = lastSlash >= 0 ? path[..lastSlash] : "";
            var fileName = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;

            if (dir != currentDir)
            {
                currentDir = dir;
                sb.AppendLine();
                sb.AppendLine($"{dir}/");
            }

            sb.AppendLine($"  {fileName}");

            var content = referencedDocument.Document.Content;

            foreach (var boundingTree in referencedDocument.BoundingTreesSorted.OrderBy(t => t.Root.StartOffset))
            {
                var root = boundingTree.Root;
                var start = root.StartOffset;
                var end = root.EndOffset;

                // Build per-range formatted text for the reviewer:
                var rangeSb = new StringBuilder();
                rangeSb.AppendLine($"{path}:{start},{end}");

                if (root.NodeType.IsHeading())
                {
                    sb.AppendLine($"    {start},{end} (@{root.Text})");
                    rangeSb.AppendLine($"  @{root.Text}");
                }
                else
                {
                    sb.AppendLine($"    {start},{end}");
                }

                var nodeText = content[start..end];

                AppendSnippets(sb, nodeText, start, tokens, config.SnippetContext, config.MaxWindows, config.DesiredSnippets);
                AppendSnippets(rangeSb, nodeText, start, tokens, config.SnippetContext, config.MaxWindows, config.DesiredSnippets);

                ranges.Add(new ContentRange(referencedDocument.Document, start, end, rangeSb.ToString()));
            }
        }

        return ranges;
    }

    /// <summary>
    ///     Searches <paramref name="nodeText"/> line-by-line for occurrences of <paramref name="tokens"/>.
    ///     For each match, captures a small window of surrounding text.
    ///     Overlapping windows on the same line are merged, then snippets are printed with their document offsets.
    ///     Regions are selected greedily to maximize token coverage so each token gets at least one representation where possible.
    /// </summary>
    internal static void AppendSnippets(
        StringBuilder sb,
        string nodeText,
        int nodeStartOffset,
        IReadOnlyList<string> tokens,
        int snippetLength,
        int maxWindows,
        int desiredSnippets)
    {
        if (nodeText.Length == 0 || tokens.Count == 0)
        {
            return;
        }

        // Per-token tracking for round-robin matching so no single frequent token hogs the entire budget:
        var searchIndices = new int[tokens.Count];
        var exhausted = new bool[tokens.Count];

        var maxSnippets = Math.Max(tokens.Count, desiredSnippets);

        var windows = new List<(int Start, int End, int TokenIndex)>();

        // Scan the node line-by-line and collect match windows:
        var lineStart = 0;
        for (var textIndex = 0; textIndex <= nodeText.Length; textIndex++)
        {
            // Only stop at line boundaries:
            if (textIndex != nodeText.Length && nodeText[textIndex] != '\n')
            {
                continue;
            }

            var lineEnd = textIndex;

            if (lineEnd > lineStart && nodeText[lineEnd - 1] == '\r')
            {
                lineEnd--;
            }

            var lineLength = lineEnd - lineStart;
            if (lineLength > 0)
            {
                searchIndices.AsSpan().Clear();
                exhausted.AsSpan().Clear();

                // One match per token per pass until the line is exhausted:
                while (windows.Count < maxWindows)
                {
                    var anyFound = false;

                    for (var tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
                    {
                        if (exhausted[tokenIndex])
                        {
                            continue;
                        }

                        var token = tokens[tokenIndex];
                        var searchFrom = lineStart + searchIndices[tokenIndex];
                        var searchLength = lineLength - searchIndices[tokenIndex];

                        // Remaining line fragment is too short to hold this token:
                        if (searchLength < token.Length)
                        {
                            exhausted[tokenIndex] = true;
                            continue;
                        }

                        var indexOfToken = nodeText.IndexOf(
                            token,
                            searchFrom,
                            searchLength,
                            StringComparison.OrdinalIgnoreCase
                        );

                        if (indexOfToken != -1)
                        {
                            // Build a window around the match, clamped to the current line:
                            var matchPosInLine = indexOfToken - lineStart;
                            var windowStart = Math.Max(0, matchPosInLine - snippetLength);
                            var windowEnd = Math.Min(lineLength, matchPosInLine + token.Length + snippetLength);

                            windows.Add((lineStart + windowStart, lineStart + windowEnd, tokenIndex));

                            // Advance past this match so the next pass finds the next occurrence:
                            searchIndices[tokenIndex] = matchPosInLine + token.Length;
                            anyFound = true;

                            if (windows.Count >= maxWindows)
                            {
                                break;
                            }
                        }
                        else
                        {
                            exhausted[tokenIndex] = true;
                        }
                    }

                    if (!anyFound)
                    {
                        break;
                    }
                }
            }

            lineStart = textIndex + 1;

            if (windows.Count >= maxWindows)
            {
                break;
            }
        }

        if (windows.Count == 0)
        {
            return;
        }

        // Merge overlapping windows and union the tokens they represent:
        windows.Sort((a, b) => a.Start.CompareTo(b.Start));

        var merged = new List<(int Start, int End, HashSet<int> TokenIndices)>();
        var (matchStart, matchEnd, matchTokens) = (windows[0].Start, windows[0].End, new HashSet<int> { windows[0].TokenIndex });
        for (var windowIndex = 1; windowIndex < windows.Count; windowIndex++)
        {
            var (windowStart, windowEnd, windowToken) = windows[windowIndex];

            if (windowStart <= matchEnd)
            {
                // Overlapping or adjacent. Extend the current merged range:
                matchEnd = Math.Max(matchEnd, windowEnd);
                matchTokens.Add(windowToken);
            }
            else
            {
                merged.Add((matchStart, matchEnd, matchTokens));
                (matchStart, matchEnd, matchTokens) = (windowStart, windowEnd, [windowToken]);
            }
        }

        merged.Add((matchStart, matchEnd, matchTokens));

        // Print up to maxSnippets, preferring regions that cover the most uncovered tokens:
        var coveredTokens = new HashSet<int>();
        var printed = 0;
        while (printed < maxSnippets && merged.Count > 0)
        {
            // Find the region with the most uncovered tokens. Tie-break by earliest start:
            var bestIndex = 0;
            var bestNewTokens = -1;
            for (var i = 0; i < merged.Count; i++)
            {
                var newTokenCount = 0;
                foreach (var t in merged[i].TokenIndices)
                {
                    if (!coveredTokens.Contains(t))
                    {
                        newTokenCount++;
                    }
                }

                if (newTokenCount > bestNewTokens)
                {
                    bestNewTokens = newTokenCount;
                    bestIndex = i;
                }
                else if (newTokenCount == bestNewTokens && newTokenCount >= 0)
                {
                    if (merged[i].Start < merged[bestIndex].Start)
                    {
                        bestIndex = i;
                    }
                }
            }

            // No region adds any new token:
            if (bestNewTokens <= 0)
            {
                break;
            }

            var region = merged[bestIndex];

            var start = region.Start;
            var end = region.End;

            // Snap to nearest word boundaries for clean output:
            while (start > 0 && !char.IsWhiteSpace(nodeText[start]))
            {
                start--;
            }

            while (end < nodeText.Length && !char.IsWhiteSpace(nodeText[end]))
            {
                end++;
            }

            while (start < end && char.IsWhiteSpace(nodeText[start]))
            {
                start++;
            }

            while (end > start && char.IsWhiteSpace(nodeText[end - 1]))
            {
                end--;
            }

            var snippet = nodeText[start..end];
            var prefix = start > 0 ? "..." : "";
            var suffix = end < nodeText.Length ? "..." : "";

            // Convert back to document offsets for fetch:
            var docStart = nodeStartOffset + start;
            var docEnd = nodeStartOffset + end;

            sb.AppendLine($"      '{prefix}{snippet}{suffix}':{docStart},{docEnd}");
            printed++;

            coveredTokens.UnionWith(region.TokenIndices);
            merged.RemoveAt(bestIndex);
        }
    }
}
