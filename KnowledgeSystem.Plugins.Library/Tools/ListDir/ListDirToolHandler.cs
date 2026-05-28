using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Retrieval.Api.Store;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Library.Tools.ListDir;

public sealed class ListDirToolHandler(
    AgentTool tool,
    StringArgument pathArgument,
    IReadOnlyDocumentStore store,
    ListDirToolConfig config
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, IReadOnlyDocumentStore store, IServiceProvider serviceProvider, ListDirToolConfig config)
    {
        var listTool = new ToolBuilder("list_dir")
            .WithDescription("Lists the immediate files and subdirectories in a directory path. Does NOT recurse into subdirectories. Use this to explore the repository structure before searching.")
            .WithRequiredStringArgument("path", "The directory path to list (e.g. 'SDX/PublicWiki/' or 'SDX/Data/'). Use empty string or '/' to list the root directory.", out var pathArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<ListDirToolHandler>(
            serviceProvider,
            listTool,
            pathArg,
            store,
            config
        );
        
        registry.RegisterTool(listTool, handler);
    }

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var path = pathArgument.GetValue(args).Trim('/');
        var prefix = string.IsNullOrEmpty(path) ? "" : path + "/";

        var documents = store.ListDocuments();
        var files = new HashSet<string>();
        var dirs = new HashSet<string>();

        foreach (var document in documents)
        {
            var docPath = document.Path;
            if (!docPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remaining = docPath[prefix.Length..];
            var slashIndex = remaining.IndexOf('/');

            if (slashIndex >= 0)
            {
                dirs.Add(remaining[..slashIndex]);
            }
            else
            {
                files.Add(remaining);
            }
        }

        var sortedDirs = dirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        var sortedFiles = files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

        var totalEntries = sortedDirs.Count + sortedFiles.Count;
        if (totalEntries == 0)
        {
            return Task.FromResult(Error($"list_dir: No documents found under '{path}'"));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# list_dir: '{path}' ({totalEntries} entries)");

        if (sortedDirs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Subdirectories:");
            foreach (var dir in sortedDirs.Take(config.MaxEntries))
            {
                sb.AppendLine($"  {dir}/");
            }
        }

        if (sortedFiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Files:");
            foreach (var file in sortedFiles.Take(config.MaxEntries))
            {
                sb.AppendLine($"  {file}");
            }
        }

        if (totalEntries > config.MaxEntries)
        {
            sb.AppendLine();
            sb.AppendLine($"... and {totalEntries - config.MaxEntries} more. Narrow your path for a more specific listing.");
        }

        return Task.FromResult(Success(sb.ToString()));
    }
}
