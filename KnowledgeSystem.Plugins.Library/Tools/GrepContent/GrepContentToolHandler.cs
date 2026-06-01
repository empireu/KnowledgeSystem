using System.Text;
using System.Text.RegularExpressions;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.Plugins.Library.Tools.Markers;
using KnowledgeSystem.Retrieval.Api.Store;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Library.Tools.GrepContent;

public sealed class GrepContentToolHandler(
    AgentTool tool,
    StringArgument queryArgument,
    StringArgument pathFilterArgument,
    IReadOnlyMarkdownDocumentStore store,
    GrepContentToolConfig config
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, IReadOnlyMarkdownDocumentStore store, IServiceProvider serviceProvider, GrepContentToolConfig config)
    {
        var grepTool = new ToolBuilder("grep_content")
            .WithDescription("Searches the text content of documents using keyword (BM25) matching. Returns file paths and character offsets where matches occur. Use this to quickly locate where specific terms appear without a full semantic search.")
            .WithRequiredStringArgument("query", "Keywords to search for. Use specific terms rather than full sentences (e.g. 'thrust MN' not 'what is the thrust in meganewtons').", out var queryArg)
            .WithStringArgument("pathFilter", "Optional case-insensitive regex that limits which file paths are searched (e.g. 'SDX/Data' or 'WeaponCore').", out var filterArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<GrepContentToolHandler>(
            serviceProvider,
            grepTool,
            queryArg,
            filterArg,
            store,
            config
        );
     
        registry.RegisterTool(grepTool, handler);
    }
    
    private readonly ILexicalMarkdownSearchCapability _lexicalCapability = store.GetCapability<ILexicalMarkdownSearchCapability>(ILexicalMarkdownSearchCapability.CapabilityType);

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var query = queryArgument.GetValue(args);
        var pathFilter = pathFilterArgument.GetValueOrNull(args);

        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(Error("grep_content: Empty query argument!"));
        }

        Regex? filterRegex = null;
        if (!string.IsNullOrEmpty(pathFilter))
        {
            try
            {
                filterRegex = new Regex(pathFilter, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1.0));
            }
            catch (ArgumentException ex)
            {
                return Task.FromResult(Error($"grep_content: Invalid pathFilter regex: {ex.Message}"));
            }
        }

        var bm25Results = _lexicalCapability.SearchBm25(query);

        if (bm25Results.Length == 0)
        {
            return Task.FromResult(Error($"grep_content: No matches found for '{query}'"));
        }

        // Group matching chunks by document path, collecting offset ranges:
        var documentGroups = new Dictionary<string, (EmdDocument Document, List<(int Start, int End, float Score, string Snippet)> Ranges)>();

        try
        {
            foreach (var bm25Result in bm25Results)
            {
                if (!store.TryGetChunk(bm25Result.ChunkId, out var chunk))
                {
                    continue;
                }

                var documentPath = chunk.Node.Document.Path;

                if (filterRegex != null && !filterRegex.IsMatch(documentPath))
                {
                    continue;
                }

                var absStart = chunk.Node.RawNode.StartOffset + chunk.StartOffset;
                var absEnd = absStart + chunk.Length;

                // Build a compact snippet from the raw content:
                var rawContent = chunk.RawContent;
                var snippet = rawContent.Length <= config.SnippetLength
                    ? rawContent
                    : rawContent[..(config.SnippetLength - 1)] + "...";
                snippet = snippet.Replace("\r", "").Replace("\n", " ");

                if (!documentGroups.TryGetValue(documentPath, out var entry))
                {
                    entry = (chunk.Node.Document, []);
                    documentGroups[documentPath] = entry;
                }

                entry.Ranges.Add((absStart, absEnd, bm25Result.Score, snippet));
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            return Task.FromResult(Error($"grep_content: Regex timed out: {ex.Message}"));
        }

        if (documentGroups.Count == 0)
        {
            var suffix = pathFilter != null ? $" matching '{pathFilter}'" : "";
            return Task.FromResult(Error($"grep_content: No matches found for '{query}'{suffix}"));
        }

        // Sort documents by their best matching score, take top N:
        var sortedDocs = documentGroups
            .OrderByDescending(kv => kv.Value.Ranges.Max(r => r.Score))
            .Take(config.MaxResults)
            .ToList();

        var sb = new StringBuilder();
        var contentRanges = new List<ContentRange>();
        var suffix2 = pathFilter != null ? $" (filter: '{pathFilter}')" : "";
        sb.AppendLine($"# grep_content: {sortedDocs.Count} file(s) matching '{query}'{suffix2}");

        string? currentDir = null;
        foreach (var (documentPath, (document, ranges)) in sortedDocs)
        {
            var lastSlash = documentPath.LastIndexOf('/');
            var dir = lastSlash >= 0 ? documentPath[..lastSlash] : "";
            var fileName = lastSlash >= 0 ? documentPath[(lastSlash + 1)..] : documentPath;

            if (dir != currentDir)
            {
                currentDir = dir;
                sb.AppendLine();
                sb.AppendLine($"{dir}/");
            }

            sb.AppendLine($"  {fileName} (score: {ranges.Max(r => r.Score):F2})");

            // Show up to N offset ranges per file with snippets:
            var sortedRanges = ranges
                .OrderByDescending(r => r.Score)
                .Take(config.MaximumRanges)
                .OrderBy(r => r.Start)
                .ToList();
            
            foreach (var (start, end, _, snippet) in sortedRanges)
            {
                sb.AppendLine($"    {start},{end} — '{snippet}'");
                contentRanges.Add(new ContentRange(document, start, end, $"{documentPath}:{start},{end} — '{snippet}'"));
            }

            if (ranges.Count > config.MaximumRanges)
            {
                sb.AppendLine($"    ... and {ranges.Count - config.MaximumRanges} more ranges");
            }
        }

        if (documentGroups.Count > config.MaxResults)
        {
            sb.AppendLine();
            sb.AppendLine($"... and {documentGroups.Count - config.MaxResults} more files. Narrow your query or use pathFilter.");
        }

        var result = sb.ToString();

        runner.ExecutionContext.Timeline.InsertElement(new GrepContentMarker
        {
            Output = result,
            Ranges = contentRanges
        });

        return Task.FromResult(Success(result));
    }
}
