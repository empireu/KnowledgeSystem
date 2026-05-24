using KnowledgeSystem.Agent.Tools.Markers;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.EmdParser.MarkdownTree;
using KnowledgeSystem.Retrieval.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Agent.Tools.RepoFetch;

public sealed class RepoFetchToolHandler(
    AgentTool tool, 
    StringArgument referenceArgument,
    RagEngine engine,
    RepoFetchToolConfig config
) : ToolHandler<ConversationalContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<ConversationalContext> registry, IServiceProvider serviceProvider, RepoFetchToolConfig config)
    {
        var fetchTool = new ToolBuilder("repo_fetch")
            .WithDescription("Fetches the content of a specific repository reference (file, definition, or offsets).")
            .WithRequiredStringArgument("reference", "The reference to fetch. Formats: 'path/to/file.md' (entire file, for small files only), 'path/to/file.md@Heading' (section under heading), 'path/to/file.md:100,200' (slice between offsets).", out var referenceArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<RepoFetchToolHandler>(
            serviceProvider,
            fetchTool,
            referenceArg,
            config
        );
        
        registry.RegisterTool(fetchTool, handler);
    }
    
    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<ConversationalContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var argument = referenceArgument.GetValue(args);

        if (!EmdReferencePath.TryParse(argument, out var refPath))
        {
            return Task.FromResult(Error(
                "Error: malformed call. Possible call formats:\n" +
                "   1. repo_fetch(\"path/to/directory\") - Displays the documents in a directory\n" +
                "   2. repo_fetch(\"path/to/file.md:offset1,offset2\") - Fetches the content from the file between the specified offsets\n" +
                "   3. repo_fetch(\"path/to/file.md@Section\") - Fetches the content under a heading\n"
            ));
        }

        var context = runner.ExecutionContext.ChatContext;
        switch (refPath.Type)
        {
            case EmdReferencePath.ReferenceType.Directory:
            {
                return Task.FromResult(Error("Cannot fetch directory."));
            }
            case EmdReferencePath.ReferenceType.File:
            {
                return Task.FromResult(RepoFetchFile(context, refPath));
            }
            case EmdReferencePath.ReferenceType.Definition:
            {
                return Task.FromResult(RepoFetchDefinition(context, refPath));
            }
            case EmdReferencePath.ReferenceType.Offsets:
            {
                return Task.FromResult(RepoFetchOffsets(context, refPath));
            }
            default:
                throw new Exception($"Unhandled ref type {refPath.Type}");
        }   
    }
    
    private ToolExecutionResult RepoFetchFile(AgentContext context, EmdReferencePath refPath)
    {
        var fileRef = refPath.GetFile();
        if (!engine.Repo.Documents.TryGetValue(fileRef, out var document))
        {
            return Error($"repo_fetch: document {fileRef.RepositoryRelativePath} not found!");
        }

        var maxChars = config.MaxChars;

        if (document.Content.Length > maxChars)
        {
            return Error($"repo_fetch: document too long ({document.Content.Length} chars, limit {maxChars}). " +
                         $"Fetch by offsets, e.g. 0,{maxChars}");
        }

        context.InsertElement(new RepositoryFetchedNodeMarker
        {
            Node = document.RootNode
        });
        
        return Success($"# Document: {document.Path}\n\n{document.Content}");
    }
    
    private ToolExecutionResult RepoFetchDefinition(AgentContext context, EmdReferencePath refPath)
    {
        var fileRef = refPath.GetFile();
        if (!engine.Repo.Documents.TryGetValue(fileRef, out var document))
        {
            return Error($"repo_fetch: document {fileRef.RepositoryRelativePath} not found!");
        }
                
        if (!document.NodesWithDefinition.TryGetValue(refPath, out var node))
        {
            var argumentTrimmed = refPath.Definition.Trim();
            node = document.AttachedNodes.Values.FirstOrDefault(x => x.RawNode.NodeType.IsHeading() && argumentTrimmed.Equals(x.RawNode.Text.Trim()));
            
            if (node == null)
            {
                var headings = string.Join("\n", document.AttachedNodes.Values.Where(x => x.RawNode.NodeType.IsHeading()).Select(x => x.RawNode.Text));
                return Error($"repo_fetch: definition not found in target file! Available headings:\n{headings}");
            }
        }

        var length = node.RawNode.EndOffset - node.RawNode.StartOffset;
        
        if (length > config.MaxChars)
        {
            return Error($"repo_fetch: definition too long. Please explore it in offset slices. Offsets of the requested section are: {node.RawNode.StartOffset},{node.RawNode.EndOffset}");
        }

        context.InsertElement(new RepositoryFetchedNodeMarker
        {
            Node = node
        });
        
        var sectionContent = document.Content.Substring(node.RawNode.StartOffset, node.RawNode.EndOffset - node.RawNode.StartOffset);
        
        return Success($"# Document: {document.Path}\n\n{sectionContent}");
    }
    
    private ToolExecutionResult RepoFetchOffsets(AgentContext context, EmdReferencePath refPath)
    {
        var fileRef = refPath.GetFile();
        if (!engine.Repo.Documents.TryGetValue(fileRef, out var document))
        {
            return Error($"repo_fetch: document {fileRef.RepositoryRelativePath} not found!");
        }

        if (refPath.StartOffset < 0 || refPath.EndOffset <= 0 || refPath.EndOffset <= refPath.StartOffset)
        {
            return Error($"repo_fetch: offsets {refPath.StartOffset},{refPath.EndOffset} are invalid! Offsets are zero-based. The first one is the start offset and is inclusive; the second one is the end offset and is exclusive.");
        }

        var end = refPath.EndOffset;

        if (end > document.Content.Length)
        {
            end = document.Content.Length;
        }

        var length = end - refPath.StartOffset;
        var maxChars = config.MaxChars;
        
        if (length > maxChars)
        {
            return Error($"repo_fetch: size limit ({maxChars}) exceeded. Please fetch a smaller number of characters.");
        }

        var content = document.Content.Substring(refPath.StartOffset, length);
        var output = $"# Document: {document.Path}\n\n{content}";
        
        context.InsertElement(new RepositoryFetchedTextMarker
        {
            Content = output
        });
        
        return Success(output);
    }
}