using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Wiki.CodeRag;

public sealed class CodeReadFileToolHandler(
    AgentTool tool,
    StringArgument pathArgument,
    IntegerArgument startLineArgument,
    IntegerArgument endLineArgument,
    CodeReposFileSystem fileSystem,
    CodeReadFileToolConfig config
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, CodeReposFileSystem fileSystem, IServiceProvider serviceProvider, CodeReadFileToolConfig config)
    {
        var readTool = new ToolBuilder("code_read_file")
            .WithDescription("Reads a file from the code repositories, optionally limited to a 1-based inclusive line range. Returns the requested lines with line numbers. Use this to inspect code after locating files with code_find_files or code_grep.")
            .WithRequiredStringArgument("path", "Repo-relative path of the file to read, e.g. 'MyProject/src/AuthService.cs'.", out var pathArg)
            .WithIntegerArgument("startLine", "First line to read (1-based, inclusive). Omit to start from the beginning of the file.", out var startLineArg)
            .WithIntegerArgument("endLine", "Last line to read (1-based, inclusive). Omit to read to the end of the file.", out var endLineArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<CodeReadFileToolHandler>(
            serviceProvider,
            readTool,
            pathArg,
            startLineArg,
            endLineArg,
            fileSystem,
            config
        );

        registry.RegisterTool(readTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var path = pathArgument.GetValue(args);
        var startLine = startLineArgument.TryGetValue(args, out var startLineValue) ? (int?)startLineValue : null;
        var endLine = endLineArgument.TryGetValue(args, out var endLineValue) ? (int?)endLineValue : null;

        if (string.IsNullOrWhiteSpace(path))
        {
            return Error("code_read_file: Empty path argument!");
        }

        if (!fileSystem.TryResolvePath(path, out var fullPath))
        {
            return Error($"code_read_file: Invalid path '{path}'. Paths must be relative to the code repos root.");
        }

        if (!File.Exists(fullPath))
        {
            return Error($"code_read_file: File '{path}' not found!");
        }

        string content;
        try
        {
            content = await File.ReadAllTextAsync(fullPath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error($"code_read_file: Failed to read file: {ex.Message}");
        }

        if (CodeReposFileSystem.IsBinaryContent(content))
        {
            return Error($"code_read_file: File '{path}' appears to be a binary file.");
        }

        var lines = CodeReposFileSystem.SplitLines(content);
        var lineCount = lines.Length;

        if (lineCount == 0)
        {
            return Success($"# code_read_file: {path} (empty file)");
        }

        if (startLine.HasValue && startLine.Value < 1)
        {
            return Error("code_read_file: startLine must be at least 1.");
        }

        if (startLine.HasValue && endLine.HasValue && startLine.Value > endLine.Value)
        {
            return Error("code_read_file: startLine must not be greater than endLine.");
        }

        if (endLine.HasValue && endLine.Value > lineCount)
        {
            endLine = lineCount;
        }

        var fromLine = startLine ?? 1;
        var toLine = endLine ?? lineCount;

        if (fromLine > lineCount)
        {
            return Error($"code_read_file: startLine {fromLine} exceeds the file's {lineCount} line(s).");
        }

        var rangeLength = toLine - fromLine + 1;

        if (rangeLength > config.MaxLines)
        {
            return Error($"code_read_file: requested range too long ({rangeLength} lines, limit {config.MaxLines}). Narrow the line range.");
        }

        var rawCharCount = 0;
        for (var i = fromLine - 1; i < toLine; i++)
        {
            rawCharCount += lines[i].Length + 1;
        }

        if (rawCharCount > config.MaxChars)
        {
            return Error($"code_read_file: requested range too long ({rawCharCount} chars, limit {config.MaxChars}). Narrow the line range.");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# code_read_file: {path} (lines {fromLine}-{toLine})");
        sb.AppendLine();

        for (var i = fromLine - 1; i < toLine; i++)
        {
            var line = lines[i];

            if (line.Length > config.MaxLineLength)
            {
                line = line[..(config.MaxLineLength - 1)] + "...";
            }

            sb.AppendLine($"  {i + 1}: {line}");
        }

        return Success(sb.ToString());
    }
}
