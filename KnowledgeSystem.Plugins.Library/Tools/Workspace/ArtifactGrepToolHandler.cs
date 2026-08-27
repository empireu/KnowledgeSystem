using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Library.Tools.Workspace;

public sealed class ArtifactGrepToolHandler(
    AgentTool tool,
    StringArgument patternArgument,
    StringArgument nameArgument,
    ArtifactWorkspace workspace
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, ArtifactWorkspace workspace, IServiceProvider serviceProvider)
    {
        var grepTool = new ToolBuilder("artifact_grep")
            .WithDescription("Searches artifact workspace files with a regular expression. Returns matching lines as 'name:line: text'. Optionally restrict the search to a single file.")
            .WithRequiredStringArgument("pattern", "The regex to search for, e.g. 'public void' or 'TODO'.", out var patternArg)
            .WithStringArgument("name", "Optional file name to restrict the search to.", out var nameArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<ArtifactGrepToolHandler>(
            serviceProvider,
            grepTool,
            patternArg,
            nameArg,
            workspace
        );

        registry.RegisterTool(grepTool, handler);
    }

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var pattern = patternArgument.GetValue(args);
        var name = nameArgument.TryGetValue(args, out var nameValue) ? nameValue : null;

        if (!workspace.TryGrep(pattern, name, out var output, out var error))
        {
            return Task.FromResult(Error($"artifact_grep: {error}"));
        }

        return Task.FromResult(Success(output));
    }
}
