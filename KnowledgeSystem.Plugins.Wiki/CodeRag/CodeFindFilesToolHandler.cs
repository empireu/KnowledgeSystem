using System.Text;
using System.Text.RegularExpressions;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Wiki.CodeRag;

public sealed class CodeFindFilesToolHandler(
    AgentTool tool,
    StringArgument patternArgument,
    CodeReposFileSystem fileSystem,
    CodeFindFilesToolConfig config
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, CodeReposFileSystem fileSystem, IServiceProvider serviceProvider, CodeFindFilesToolConfig config)
    {
        var findTool = new ToolBuilder("code_find_files")
            .WithDescription("Searches for files by name using a case-insensitive regex pattern on the full repo-relative path. Use this to locate files in the code repositories when you know part of the filename or path.")
            .WithRequiredStringArgument("pattern", "Regex pattern to match against repo-relative file paths. Examples: 'AuthService' matches any file with 'AuthService' in its path; '.*\\.cs$' matches C# files; 'MyProject/src/.*\\.ts$' matches TypeScript files under MyProject/src.", out var patternArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<CodeFindFilesToolHandler>(
            serviceProvider,
            findTool,
            patternArg,
            fileSystem,
            config
        );

        registry.RegisterTool(findTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var pattern = patternArgument.GetValue(args);

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Error("code_find_files: Empty pattern argument!");
        }

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1.0));
        }
        catch (ArgumentException ex)
        {
            return Error($"code_find_files: Invalid regex pattern: {ex.Message}");
        }

        var results = new List<string>();

        try
        {
            foreach (var filePath in fileSystem.EnumerateFiles(""))
            {
                if (regex.IsMatch(filePath))
                {
                    results.Add(filePath);
                }
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            return Error($"code_find_files: Regex timed out: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error($"code_find_files: Failed to enumerate files: {ex.Message}");
        }

        if (results.Count == 0)
        {
            return Error($"code_find_files: No files matching '{pattern}'");
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine($"# code_find_files: {results.Count} file(s) matching '{pattern}'");

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

        return Success(sb.ToString());
    }
}
