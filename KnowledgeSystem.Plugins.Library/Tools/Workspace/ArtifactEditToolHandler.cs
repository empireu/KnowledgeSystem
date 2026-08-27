using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Library.Tools.Workspace;

public sealed class ArtifactEditToolHandler(
    AgentTool tool,
    StringArgument nameArgument,
    StringArgument findArgument,
    StringArgument replaceArgument,
    ArtifactWorkspace workspace
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, ArtifactWorkspace workspace, IServiceProvider serviceProvider)
    {
        var editTool = new ToolBuilder("artifact_edit")
            .WithDescription("Edits a file by replacing an exact string with another. The find text must match exactly once in the file; if it appears multiple times, include more surrounding lines in the find text to make it unique. Whitespace and indentation must match exactly (spaces, not tabs). On failure, read or grep the file and retry with corrected find text.")
            .WithRequiredStringArgument("name", "File name in the artifact workspace.", out var nameArg)
            .WithRequiredStringArgument("find", "The exact text to replace, including enough surrounding context to be unique.", out var findArg)
            .WithRequiredStringArgument("replace", "The replacement text.", out var replaceArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<ArtifactEditToolHandler>(
            serviceProvider,
            editTool,
            nameArg,
            findArg,
            replaceArg,
            workspace
        );

        registry.RegisterTool(editTool, handler);
    }

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var name = nameArgument.GetValue(args);
        var find = findArgument.GetValue(args);
        var replace = replaceArgument.GetValue(args);

        if (!workspace.TryEdit(name, find, replace, out var error, out var line))
        {
            return Task.FromResult(Error($"artifact_edit: {error}"));
        }

        return Task.FromResult(Success($"artifact_edit: replaced at line {line} in '{name}'."));
    }
}
