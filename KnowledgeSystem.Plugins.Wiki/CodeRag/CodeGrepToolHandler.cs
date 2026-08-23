using System.Text;
using System.Text.RegularExpressions;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Wiki.CodeRag;

public sealed class CodeGrepToolHandler(
    AgentTool tool,
    StringArgument patternArgument,
    StringArgument pathArgument,
    StringArgument pathFilterArgument,
    CodeReposFileSystem fileSystem,
    CodeGrepToolConfig config
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, CodeReposFileSystem fileSystem, IServiceProvider serviceProvider, CodeGrepToolConfig config)
    {
        var grepTool = new ToolBuilder("code_grep")
            .WithDescription("Searches the content of files in the code repositories using a case-insensitive regex pattern. Returns file paths, line numbers, and short snippets of matching lines. Use this to locate where specific code or terms appear.")
            .WithRequiredStringArgument("pattern", "Regex pattern to search for in file contents, e.g. 'ILogger' or 'TODO:.*fix'.", out var patternArg)
            .WithRequiredStringArgument("path", "Repo-relative directory to search under, e.g. 'MyProject/src'. Use an empty string to search the whole code repos root.", out var pathArg)
            .WithStringArgument("pathFilter", "Optional case-insensitive regex that limits which file paths are searched, e.g. '.*\\.cs$'.", out var filterArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<CodeGrepToolHandler>(
            serviceProvider,
            grepTool,
            patternArg,
            pathArg,
            filterArg,
            fileSystem,
            config
        );

        registry.RegisterTool(grepTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var pattern = patternArgument.GetValue(args);
        var path = pathArgument.GetValue(args);
        var pathFilter = pathFilterArgument.GetValueOrNull(args);

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Error("code_grep: Empty pattern argument!");
        }

        Regex patternRegex;
        Regex? filterRegex = null;

        try
        {
            patternRegex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1.0));

            if (!string.IsNullOrEmpty(pathFilter))
            {
                filterRegex = new Regex(pathFilter, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1.0));
            }
        }
        catch (ArgumentException ex)
        {
            return Error($"code_grep: Invalid regex pattern: {ex.Message}");
        }

        var fileGroups = new Dictionary<string, (List<(int Line, string Snippet)> Matches, int MatchCount)>();

        try
        {
            foreach (var filePath in fileSystem.EnumerateFiles(path))
            {
                if (filterRegex != null && !filterRegex.IsMatch(filePath))
                {
                    continue;
                }

                if (!fileSystem.TryResolvePath(filePath, out var fullPath))
                {
                    continue;
                }

                try
                {
                    var fileInfo = new FileInfo(fullPath);
                    if (fileInfo.Length > config.MaxFileBytes)
                    {
                        continue;
                    }

                    var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
                    if (CodeReposFileSystem.IsBinaryContent(content))
                    {
                        continue;
                    }

                    var lines = CodeReposFileSystem.SplitLines(content);
                    var matches = new List<(int Line, string Snippet)>();
                    var matchCount = 0;

                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (!patternRegex.IsMatch(lines[i]))
                        {
                            continue;
                        }

                        matchCount++;

                        if (matches.Count >= config.MaxMatchesPerFile)
                        {
                            continue;
                        }

                        var snippet = lines[i];
                        if (snippet.Length > config.SnippetLength)
                        {
                            snippet = snippet[..(config.SnippetLength - 1)] + "...";
                        }

                        matches.Add((i + 1, snippet));
                    }

                    if (matchCount > 0)
                    {
                        fileGroups[filePath] = (matches, matchCount);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            return Error($"code_grep: Regex timed out: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error($"code_grep: Failed to search files: {ex.Message}");
        }

        if (fileGroups.Count == 0)
        {
            var suffix = pathFilter != null ? $" matching '{pathFilter}'" : "";
            return Error($"code_grep: No matches found for '{pattern}' in '{path}'{suffix}");
        }

        var sortedFiles = fileGroups
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(config.MaxResults)
            .ToList();

        var sb = new StringBuilder();
        var header = $"# code_grep: {sortedFiles.Count} file(s) matching '{pattern}' in '{path}'";

        if (pathFilter != null)
        {
            header += $" (filter: '{pathFilter}')";
        }

        sb.AppendLine(header);

        string? currentDir = null;

        foreach (var (filePath, (matches, matchCount)) in sortedFiles)
        {
            var lastSlash = filePath.LastIndexOf('/');
            var dir = lastSlash >= 0 ? filePath[..lastSlash] : "";
            var fileName = lastSlash >= 0 ? filePath[(lastSlash + 1)..] : filePath;

            if (dir != currentDir)
            {
                currentDir = dir;
                sb.AppendLine();
                sb.AppendLine($"{dir}/");
            }

            sb.AppendLine($"  {fileName} ({matchCount} matches)");

            foreach (var (line, snippet) in matches)
            {
                sb.AppendLine($"    {line} — '{snippet}'");
            }

            if (matchCount > config.MaxMatchesPerFile)
            {
                sb.AppendLine($"    ... and {matchCount - config.MaxMatchesPerFile} more matches");
            }
        }

        if (fileGroups.Count > config.MaxResults)
        {
            sb.AppendLine();
            sb.AppendLine($"... and {fileGroups.Count - config.MaxResults} more files. Narrow your pattern or path.");
        }

        return Success(sb.ToString());
    }
}
