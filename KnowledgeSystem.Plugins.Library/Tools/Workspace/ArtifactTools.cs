using KnowledgeSystem.Agents.Orchestration.Tools;

namespace KnowledgeSystem.Plugins.Library.Tools.Workspace;

public static class ArtifactTools
{
    public static void RegisterAll(AgentToolRegistry<BasicContext> registry, ArtifactWorkspace workspace, IServiceProvider serviceProvider)
    {
        ArtifactWriteToolHandler.Register(registry, workspace, serviceProvider);
        ArtifactEditToolHandler.Register(registry, workspace, serviceProvider);
        ArtifactReadToolHandler.Register(registry, workspace, serviceProvider, workspace.Config);
        ArtifactGrepToolHandler.Register(registry, workspace, serviceProvider);
        ArtifactListToolHandler.Register(registry, workspace, serviceProvider);
        ArtifactAttachToolHandler.Register(registry, workspace, serviceProvider);
        ArtifactDeleteToolHandler.Register(registry, workspace, serviceProvider);
    }
}
