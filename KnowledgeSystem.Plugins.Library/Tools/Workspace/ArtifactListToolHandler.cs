using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Library.Tools.Workspace;

public sealed class ArtifactListToolHandler(
    AgentTool tool,
    ArtifactWorkspace workspace
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, ArtifactWorkspace workspace, IServiceProvider serviceProvider)
    {
        var listTool = new ToolBuilder("artifact_list")
            .WithDescription("Lists the files in the artifact workspace with their sizes and line counts. Use this to discover what is stored before reading or attaching.")
            .Build();

        var handler = ActivatorUtilities.CreateInstance<ArtifactListToolHandler>(
            serviceProvider,
            listTool,
            workspace
        );

        registry.RegisterTool(listTool, handler);
    }

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var files = workspace.List();

        if (files.Count == 0)
        {
            return Task.FromResult(Success("artifact_list: workspace is empty."));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"artifact_list: {files.Count} file(s)");

        foreach (var file in files)
        {
            sb.AppendLine($"{file.Name} ({file.Size} chars, {file.LineCount} lines)");
        }

        return Task.FromResult(Success(sb.ToString().TrimEnd()));
    }
}
