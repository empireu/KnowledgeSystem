using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.EmdParser.MarkdownTree;
using KnowledgeSystem.Retrieval.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Agent;

public sealed class TreeToolHandler(
    AgentTool tool,
    StringArgument pathArgument,
    BooleanArgument detailedArgument,
    IServiceProvider serviceProvider
) : ToolHandler<ConversationalContext>.Plain(tool) 
{
    public static void Register(AgentToolRegistry<ConversationalContext> registry, IServiceProvider serviceProvider)
    {
        var treeTool = new ToolBuilder("tree")
            .WithDescription("Lists the document tree of the knowledge repository. Use this to explore what documents exist before searching.")
            .WithRequiredStringArgument("path", "The directory path prefix to list (e.g. 'docs/' or '' for root).", out var pathArg)
            .WithBooleanArgument("detailed", "If true, the results will also display the headings inside each document. Don't use it unless your path is very targeted.", out var detailedArg)
            .Build();

        var handler = new TreeToolHandler(treeTool, pathArg, detailedArg, serviceProvider);

        registry.RegisterTool(treeTool, handler);
    }

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<ConversationalContext> runner, ArgumentExtractionResult args, ConversationalContext runContext, CancellationToken cancellationToken)
    {
        var path = pathArgument.GetValue(args).Trim('/');
        
        if(!detailedArgument.TryGetValue(args, out var detailed))
        {
            detailed = false;
        }
        
        var engine = serviceProvider.GetRequiredService<RagEngine>();
        var repo = engine.Repo;

        var documents = repo.Documents.Values
            .Where(d => string.IsNullOrEmpty(path) || d.Path.StartsWith(path, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (documents.Count == 0)
        {
            return Task.FromResult(Error($"No documents found under '{path}'"));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# tree: {documents.Count} documents under '{(string.IsNullOrEmpty(path) ? "/" : path)}'");

        string? currentDir = null;
        foreach (var document in documents)
        {
            var lastSlash = document.Path.LastIndexOf('/');
            var dir = lastSlash >= 0 ? document.Path[..lastSlash] : "";

            if (dir != currentDir)
            {
                currentDir = dir;
                sb.AppendLine();
                sb.AppendLine($"{dir}/");
            }

            var fileName = lastSlash >= 0 
                ? document.Path[(lastSlash + 1)..]
                : document.Path;
            
            sb.AppendLine($"  {fileName}");

            if (detailed)
            {
                BuildHeadingTree(sb, document.RootNode.RawNode, "   ");
            }
        }

        return Task.FromResult(Success(sb.ToString()));
    }

    private static void BuildHeadingTree(StringBuilder sb, MarkdownNode node, string prefix)
    {
        foreach (var child in node.Children)
        {
            if (child.HeadingLevel > 0)
            {
                var indent = new string(' ', (child.HeadingLevel - 1) * 2);
                sb.AppendLine($"{prefix}{indent}§ {child.Text}");
            }

            BuildHeadingTree(sb, child, prefix);
        }
    }
}
