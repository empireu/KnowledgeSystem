using System.Text;
using System.Text.RegularExpressions;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Retrieval.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Agent.Tools.GrepContent;

public sealed class GrepContentToolHandler(
    AgentTool tool,
    StringArgument queryArgument,
    StringArgument pathFilterArgument,
    RagEngine engine,
    GrepContentToolConfig config
) : ToolHandler<ConversationalContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<ConversationalContext> registry, IServiceProvider serviceProvider, GrepContentToolConfig config)
    {
        var grepTool = new ToolBuilder("grep_content")
            .WithDescription("Searches the text content of documents using keyword (BM25) matching. Returns file paths and character offsets where matches occur. Use this to quickly locate where specific terms appear without a full semantic search.")
            .WithRequiredStringArgument("query", "Keywords to search for. Use specific terms rather than full sentences (e.g. 'thrust MN' not 'what is the thrust in meganewtons').", out var queryArg)
            .WithStringArgument("pathFilter", "Optional case-insensitive regex that limits which file paths are searched (e.g. 'SDX/Data' or 'WeaponCore').", out var filterArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<GrepContentToolHandler>(serviceProvider, grepTool, queryArg, filterArg, config);
     
        registry.RegisterTool(grepTool, handler);
    }

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<ConversationalContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
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
                filterRegex = new Regex(pathFilter, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
            catch (ArgumentException ex)
            {
                return Task.FromResult(Error($"grep_content: Invalid pathFilter regex: {ex.Message}"));
            }
        }

        var bm25Results = engine.LexicalIndex.SearchBm25(query);

        if (bm25Results.Length == 0)
        {
            return Task.FromResult(Error($"grep_content: No matches found for '{query}'"));
        }

        // Group matching chunks by document path, collecting offset ranges:
        var documentGroups = new Dictionary<string, List<(int Start, int End, float Score, string Snippet)>>();

        foreach (var bm25Result in bm25Results)
        {
            if (!engine.ChunkByHnswId.TryGetValue(bm25Result.HnswId, out var chunk))
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
                : rawContent[..(config.SnippetLength - 1)] + "…";
            snippet = snippet.Replace("\r", "").Replace("\n", " ");

            if (!documentGroups.TryGetValue(documentPath, out var ranges))
            {
                ranges = [];
                documentGroups[documentPath] = ranges;
            }

            ranges.Add((absStart, absEnd, bm25Result.Score, snippet));
        }

        if (documentGroups.Count == 0)
        {
            var suffix = pathFilter != null ? $" matching '{pathFilter}'" : "";
            return Task.FromResult(Error($"grep_content: No matches found for '{query}'{suffix}"));
        }

        // Sort documents by their best matching score, take top N:
        var sortedDocs = documentGroups
            .OrderByDescending(kv => kv.Value.Max(r => r.Score))
            .Take(config.MaxResults)
            .ToList();

        var sb = new StringBuilder();
        var suffix2 = pathFilter != null ? $" (filter: '{pathFilter}')" : "";
        sb.AppendLine($"# grep_content: {sortedDocs.Count} file(s) matching '{query}'{suffix2}");

        string? currentDir = null;
        foreach (var (docPath, ranges) in sortedDocs)
        {
            var lastSlash = docPath.LastIndexOf('/');
            var dir = lastSlash >= 0 ? docPath[..lastSlash] : "";
            var fileName = lastSlash >= 0 ? docPath[(lastSlash + 1)..] : docPath;

            if (dir != currentDir)
            {
                currentDir = dir;
                sb.AppendLine();
                sb.AppendLine($"{dir}/");
            }

            sb.AppendLine($"  {fileName} (score: {ranges.Max(r => r.Score):F2})");

            // Show up to 3 offset ranges per file with snippets:
            var sortedRanges = ranges.OrderBy(r => r.Start).Take(3).ToList();
            foreach (var (start, end, _, snippet) in sortedRanges)
            {
                sb.AppendLine($"    {start},{end} — '{snippet}'");
            }

            if (ranges.Count > 3)
            {
                sb.AppendLine($"    ... and {ranges.Count - 3} more ranges");
            }
        }

        if (documentGroups.Count > config.MaxResults)
        {
            sb.AppendLine();
            sb.AppendLine($"... and {documentGroups.Count - config.MaxResults} more files. Narrow your query or use pathFilter.");
        }

        return Task.FromResult(Success(sb.ToString()));
    }
}
