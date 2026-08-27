using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Events.Api;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Library.Tools.Workspace;

public sealed class ArtifactAttachToolHandler(
    AgentTool tool,
    StringArgument nameArgument,
    ArtifactWorkspace workspace
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, ArtifactWorkspace workspace, IServiceProvider serviceProvider)
    {
        var attachTool = new ToolBuilder("artifact_attach")
            .WithDescription("Queues a workspace file to be attached to the final message as-is. The file is not modified and stays in the workspace for further editing. Call it whenever the file is ready to ship.")
            .WithRequiredStringArgument("name", "File name in the artifact workspace.", out var nameArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<ArtifactAttachToolHandler>(
            serviceProvider,
            attachTool,
            nameArg,
            workspace
        );

        registry.RegisterTool(attachTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var name = nameArgument.GetValue(args);

        if (!workspace.TryGet(name, out var content) || content is null)
        {
            return Error($"artifact_attach: File '{name}' not found. Use artifact_list to see the available files.");
        }

        await runner.EventManager.SendAsync(new AddAttachmentEvent(name, content), cancellationToken);

        return Success($"artifact_attach: '{name}' queued ({content.Length} chars). It will be attached to the final message.");
    }
}
