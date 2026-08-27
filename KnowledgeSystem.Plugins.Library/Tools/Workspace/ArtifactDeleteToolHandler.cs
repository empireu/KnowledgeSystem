using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Library.Tools.Workspace;

public sealed class ArtifactDeleteToolHandler(
    AgentTool tool,
    StringArgument nameArgument,
    ArtifactWorkspace workspace
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, ArtifactWorkspace workspace, IServiceProvider serviceProvider)
    {
        var deleteTool = new ToolBuilder("artifact_delete")
            .WithDescription("Deletes a file from the artifact workspace. Use it to remove scratch files or free workspace space.")
            .WithRequiredStringArgument("name", "File name in the artifact workspace.", out var nameArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<ArtifactDeleteToolHandler>(
            serviceProvider,
            deleteTool,
            nameArg,
            workspace
        );

        registry.RegisterTool(deleteTool, handler);
    }

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var name = nameArgument.GetValue(args);

        if (!workspace.TryDelete(name, out var error))
        {
            return Task.FromResult(Error($"artifact_delete: {error}"));
        }

        return Task.FromResult(Success($"artifact_delete: deleted '{name}'."));
    }
}
