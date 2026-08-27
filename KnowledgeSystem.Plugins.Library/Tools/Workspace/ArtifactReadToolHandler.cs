using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Library.Tools.Workspace;

public sealed class ArtifactReadToolHandler(
    AgentTool tool,
    StringArgument nameArgument,
    IntegerArgument startLineArgument,
    IntegerArgument endLineArgument,
    ArtifactWorkspace workspace,
    ArtifactWorkspaceConfig config
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, ArtifactWorkspace workspace, IServiceProvider serviceProvider, ArtifactWorkspaceConfig config)
    {
        var readTool = new ToolBuilder("artifact_read")
            .WithDescription("Reads a file from the artifact workspace, optionally limited to a 1-based inclusive line range. Returns the requested lines with line numbers.")
            .WithRequiredStringArgument("name", "File name in the artifact workspace.", out var nameArg)
            .WithIntegerArgument("startLine", "First line to read (1-based, inclusive). Omit to start from the beginning of the file.", out var startLineArg)
            .WithIntegerArgument("endLine", "Last line to read (1-based, inclusive). Omit to read to the end of the file.", out var endLineArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<ArtifactReadToolHandler>(
            serviceProvider,
            readTool,
            nameArg,
            startLineArg,
            endLineArg,
            workspace,
            config
        );

        registry.RegisterTool(readTool, handler);
    }

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var name = nameArgument.GetValue(args);
        var startLine = startLineArgument.TryGetValue(args, out var startLineValue) ? (int?)startLineValue : null;
        var endLine = endLineArgument.TryGetValue(args, out var endLineValue) ? (int?)endLineValue : null;

        if (!workspace.TryGet(name, out var content) || content is null)
        {
            return Task.FromResult(Error($"artifact_read: File '{name}' not found. Use artifact_list to see the available files."));
        }

        var lines = ArtifactWorkspace.SplitLines(content);
        var lineCount = lines.Length;

        if (lineCount == 0)
        {
            return Task.FromResult(Success($"# artifact_read: {name} (empty file)"));
        }

        if (startLine.HasValue && startLine.Value < 1)
        {
            return Task.FromResult(Error("artifact_read: startLine must be at least 1."));
        }

        if (startLine.HasValue && endLine.HasValue && startLine.Value > endLine.Value)
        {
            return Task.FromResult(Error("artifact_read: startLine must not be greater than endLine."));
        }

        if (endLine.HasValue && endLine.Value > lineCount)
        {
            endLine = lineCount;
        }

        var fromLine = startLine ?? 1;
        var toLine = endLine ?? lineCount;

        if (fromLine > lineCount)
        {
            return Task.FromResult(Error($"artifact_read: startLine {fromLine} exceeds the file's {lineCount} line(s)."));
        }

        var rangeLength = toLine - fromLine + 1;

        if (rangeLength > config.MaxReadLines)
        {
            return Task.FromResult(Error($"artifact_read: requested range too long ({rangeLength} lines, limit {config.MaxReadLines}). Narrow the line range."));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# artifact_read: {name} (lines {fromLine}-{toLine})");
        sb.AppendLine();

        for (var i = fromLine - 1; i < toLine; i++)
        {
            var line = lines[i];

            if (line.Length > config.MaxLineChars)
            {
                line = line[..(config.MaxLineChars - 1)] + "...";
            }

            sb.AppendLine($"  {i + 1}: {line}");
        }

        return Task.FromResult(Success(sb.ToString()));
    }
}
