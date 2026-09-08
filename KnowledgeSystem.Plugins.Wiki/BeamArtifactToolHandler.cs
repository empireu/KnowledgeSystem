using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using KnowledgeSystem.Plugins.Library.Tools.Workspace;
using KnowledgeSystem.Plugins.Wiki.CodeRag;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Wiki;

public sealed class BeamArtifactToolHandler(
    AgentTool tool,
    StringArgument nameArgument,
    StringArgument pathArgument,
    ArtifactWorkspace workspace,
    CodeReposFileSystem wikiFileSystem
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, ArtifactWorkspace workspace, string wikiRootPath, IServiceProvider serviceProvider)
    {
        var beamTool = new ToolBuilder("beam_artifact")
            .WithDescription("Writes an artifact from the in-memory workspace to a file on disk inside the wiki repository. Creates the file if it does not exist, otherwise overwrites it. Use this to publish finished content such as new wiki entries or skill files. The written file only becomes searchable after the wiki index is reloaded.")
            .WithRequiredStringArgument("name", "Name of the artifact in the workspace to publish, e.g. 'ship-design.md'. List workspace files with artifact_list.", out var nameArg)
            .WithRequiredStringArgument("path", "Destination path relative to the wiki root, e.g. 'skills/ship-design.md' or 'guides/combat.md'. The path must stay inside the wiki root and must end with the '.md' extension because the wiki only indexes markdown files.", out var pathArg)
            .Build();

        var rootFileSystem = new CodeReposFileSystem(wikiRootPath);

        var handler = ActivatorUtilities.CreateInstance<BeamArtifactToolHandler>(
            serviceProvider,
            beamTool,
            nameArg,
            pathArg,
            workspace,
            rootFileSystem
        );

        registry.RegisterTool(beamTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var name = nameArgument.GetValue(args);
        var path = pathArgument.GetValue(args);

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error("beam_artifact: Empty artifact name!");
        }

        if (!workspace.TryGet(name, out var content) || content is null)
        {
            return Error($"beam_artifact: Artifact '{name}' not found in the workspace. List workspace files with artifact_list.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return Error("beam_artifact: Empty path argument!");
        }

        if (path.EndsWith('/') || path.EndsWith('\\'))
        {
            return Error($"beam_artifact: '{path}' is a directory. Provide a file path.");
        }

        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return Error($"beam_artifact: '{path}' must end with the '.md' extension because the wiki only indexes markdown files.");
        }

        if (!wikiFileSystem.TryResolvePath(path, out var fullPath))
        {
            return Error($"beam_artifact: Invalid path '{path}'. The path must be relative to the wiki root and must not escape it.");
        }

        try
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(fullPath, content, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Error($"beam_artifact: Failed to write '{path}'. The wiki repository may be read-only or unavailable.");
        }

        return Success($"beam_artifact: Wrote artifact '{name}' to '{path}' ({content.Length} chars). The wiki index will include this file after a wiki reload.");
    }
}
