using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Library.Tools.Workspace;

public sealed class ArtifactWriteToolHandler(
    AgentTool tool,
    StringArgument nameArgument,
    StringArgument contentArgument,
    ArtifactWorkspace workspace
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, ArtifactWorkspace workspace, IServiceProvider serviceProvider)
    {
        var writeTool = new ToolBuilder("artifact_write")
            .WithDescription("Creates or fully replaces a file in the in-memory artifact workspace with the given content. Files persist for the whole conversation. Use artifact_edit for targeted changes instead of rewriting the whole file.")
            .WithRequiredStringArgument("name", "File name, e.g. 'script.cs' or 'report.md'. Letters, digits, '.', '_' and '-' only; no folders.", out var nameArg)
            .WithRequiredStringArgument("content", "The full file content.", out var contentArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<ArtifactWriteToolHandler>(
            serviceProvider,
            writeTool,
            nameArg,
            contentArg,
            workspace
        );

        registry.RegisterTool(writeTool, handler);
    }

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var name = nameArgument.GetValue(args);
        var content = contentArgument.GetValue(args);

        if (!workspace.TryWrite(name, content, out var error))
        {
            return Task.FromResult(Error($"artifact_write: {error}"));
        }

        return Task.FromResult(Success($"artifact_write: wrote '{name}' ({content.Length} chars)."));
    }
}
