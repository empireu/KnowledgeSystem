using System.Text;
using System.Text.RegularExpressions;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Retrieval.Engine;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator

namespace KnowledgeSystem.Agent.Tools.FindFiles;

public sealed class FindFilesToolHandler(
    AgentTool tool,
    StringArgument patternArgument,
    StringArgument pathFilterArgument,
    RagEngine engine,
    FindFilesToolConfig config
) : ToolHandler<ConversationalContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<ConversationalContext> registry, IServiceProvider serviceProvider, FindFilesToolConfig config)
    {
        var findTool = new ToolBuilder("find_files")
            .WithDescription("Searches for files by name using a case-insensitive regex pattern on the full file path. Use this to locate files when you know part of the filename or path.")
            .WithRequiredStringArgument("pattern", "Regex pattern to match against file paths. Examples: 'navos' matches any file with 'navos' in its path; '.*stats\\.md$' matches files ending in 'stats.md'; 'guides/' matches files inside any 'guides' directory.", out var patternArg)
            .WithStringArgument("pathFilter", "Optional path prefix to narrow the search. Only files under this prefix are considered (e.g. 'SDX/Data/').", out var filterArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<FindFilesToolHandler>(serviceProvider, findTool, patternArg, filterArg, config);
        registry.RegisterTool(findTool, handler);
    }

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<ConversationalContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var pattern = patternArgument.GetValue(args);
        var pathFilter = pathFilterArgument.GetValueOrNull(args);

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Task.FromResult(Error("find_files: Empty pattern argument!"));
        }

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(Error($"find_files: Invalid regex pattern: {ex.Message}"));
        }

        var repo = engine.Repo;
        var results = new List<string>();

        foreach (var key in repo.Documents.Keys)
        {
            var documentPath = key.RepositoryRelativePath;

            if (!string.IsNullOrEmpty(pathFilter) &&
                !documentPath.StartsWith(pathFilter.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase) &&
                !documentPath.Equals(pathFilter.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (regex.IsMatch(documentPath))
            {
                results.Add(documentPath);
            }
        }

        if (results.Count == 0)
        {
            var suffix = pathFilter != null ? $" under '{pathFilter}'" : "";
            return Task.FromResult(Error($"find_files: No files matching '{pattern}'{suffix}"));
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        var suffix2 = pathFilter != null ? $" under '{pathFilter}'" : "";
        sb.AppendLine($"# find_files: {results.Count} file(s) matching '{pattern}'{suffix2}");

        string? currentDir = null;
        var count = 0;
        foreach (var result in results)
        {
            if (count >= config.MaxResults)
            {
                sb.AppendLine();
                sb.AppendLine($"... and {results.Count - config.MaxResults} more. Narrow your pattern or use pathFilter.");
                break;
            }

            var lastSlash = result.LastIndexOf('/');
            var dir = lastSlash >= 0 ? result[..lastSlash] : "";

            if (dir != currentDir)
            {
                currentDir = dir;
                sb.AppendLine();
                sb.AppendLine($"{dir}/");
            }

            var fileName = lastSlash >= 0 ? result[(lastSlash + 1)..] : result;
            sb.AppendLine($"  {fileName}");
            count++;
        }

        return Task.FromResult(Success(sb.ToString()));
    }
}
