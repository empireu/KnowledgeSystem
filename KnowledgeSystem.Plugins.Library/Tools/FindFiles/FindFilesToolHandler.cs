using System.Text;
using System.Text.RegularExpressions;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Retrieval.Api.Store;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator

namespace KnowledgeSystem.Plugins.Library.Tools.FindFiles;

public sealed class FindFilesToolHandler(
    AgentTool tool,
    StringArgument patternArgument,
    IReadOnlyMarkdownDocumentStore store,
    FindFilesToolConfig config
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, IReadOnlyMarkdownDocumentStore store, IServiceProvider serviceProvider, FindFilesToolConfig config)
    {
        var findTool = new ToolBuilder("find_files")
            .WithDescription("Searches for files by name using a case-insensitive regex pattern on the full file path. Use this to locate files when you know part of the filename or path.")
            .WithRequiredStringArgument("pattern", "Regex pattern to match against file paths. Examples: 'navos' matches any file with 'navos' in its path; '.*stats\\.md$' matches files ending in 'stats.md'; 'SDX/Data.*\\.md$' matches markdown files under SDX/Data.", out var patternArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<FindFilesToolHandler>(
            serviceProvider,
            findTool,
            patternArg,
            store,
            config
        );
        
        registry.RegisterTool(findTool, handler);
    }

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var pattern = patternArgument.GetValue(args);

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Task.FromResult(Error("find_files: Empty pattern argument!"));
        }

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1.0));
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(Error($"find_files: Invalid regex pattern: {ex.Message}"));
        }

        var documents = store.ListDocuments();
        var results = new List<string>();

        try
        {
            foreach (var document in documents)
            {
                var documentPath = document.Path;

                if (regex.IsMatch(documentPath))
                {
                    results.Add(documentPath);
                }
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            return Task.FromResult(Error($"find_files: Regex timed out: {ex.Message}"));
        }

        if (results.Count == 0)
        {
            return Task.FromResult(Error($"find_files: No files matching '{pattern}'"));
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine($"# find_files: {results.Count} file(s) matching '{pattern}'");

        string? currentDir = null;
        var count = 0;
        foreach (var result in results)
        {
            if (count >= config.MaxResults)
            {
                sb.AppendLine();
                sb.AppendLine($"... and {results.Count - config.MaxResults} more. Narrow your pattern.");
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
