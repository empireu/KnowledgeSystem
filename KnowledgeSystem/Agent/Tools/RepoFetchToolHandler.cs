using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.EmdParser.MarkdownTree;
using KnowledgeSystem.Retrieval.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Agent.Tools;

public sealed class RepoFetchToolHandler(
    AgentTool tool, 
    StringArgument referenceArgument,
    RagEngine engine,
    int maxChars
) : ToolHandler<ConversationalContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<ConversationalContext> registry, IServiceProvider serviceProvider, int maxChars)
    {
        var fetchTool = new ToolBuilder("repo_fetch")
            .WithDescription("Fetches the content of a specific repository reference (file, definition, directory, or offsets).")
            .WithRequiredStringArgument("reference", "The reference to fetch. Formats: 'path/to/file.md@Heading' (section under heading), 'path/to/file.md:100,200' (slice between offsets), or 'path/to/dir/' (directory listing).", out var referenceArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<RepoFetchToolHandler>(
            serviceProvider,
            fetchTool,
            referenceArg,
            maxChars
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

        switch (refPath.Type)
        {
            case EmdReferencePath.ReferenceType.Directory:
            {
                return Task.FromResult(RepoFetchDirectory(refPath, argument));
            }
            case EmdReferencePath.ReferenceType.File:
            {
                return Task.FromResult(RepoFetchFile(refPath));
            }
            case EmdReferencePath.ReferenceType.Definition:
            {
                return Task.FromResult(RepoFetchDefinition(refPath));
            }
            case EmdReferencePath.ReferenceType.Offsets:
            {
                return Task.FromResult(RepoFetchOffsets(refPath));
            }
            default:
                throw new Exception($"Unhandled ref type {refPath.Type}");
        }   
    }

    private ToolExecutionResult RepoFetchDirectory(EmdReferencePath refPath, string argument)
    {
        var directory = refPath.RepositoryRelativePath;
        var documents = engine.Repo.Documents
            .Where(x => x.Key.RepositoryRelativePath.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .OrderBy(x => x.Content.Length)
            .ToList();

        if (documents.Count == 0)
        {
            return Error($"repo_fetch: Directory \"{argument}\" not found!");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"repo_fetch: Found {documents.Count} documents in \"{argument}\":");

        foreach (var document in documents)
        {
            sb.AppendLine($"  - {document.Path} - {document.Content.Length} chars");
        }
                
        return Success(sb.ToString());
    }
    
    private ToolExecutionResult RepoFetchFile(EmdReferencePath refPath)
    {
        var fileRef = refPath.GetFile();
        if (!engine.Repo.Documents.TryGetValue(fileRef, out var document))
        {
            return Error($"repo_fetch: document {fileRef.RepositoryRelativePath} not found!");
        }

        if (document.Content.Length > maxChars)
        {
            return Error($"repo_fetch: document too long ({document.Content.Length} chars, limit {maxChars}). " +
                         $"Fetch by offsets, e.g. 0,{maxChars}");
        }

        return Success($"# Document: {document.Path}\n\n{document.Content}");
    }
    
    private ToolExecutionResult RepoFetchDefinition(EmdReferencePath refPath)
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

        if (length > maxChars)
        {
            return Error($"repo_fetch: definition too long. Please explore it in offset slices. Offsets of the requested section are: {node.RawNode.StartOffset},{node.RawNode.EndOffset}");
        }

        var sectionContent = document.Content.Substring(node.RawNode.StartOffset, node.RawNode.EndOffset - node.RawNode.StartOffset);
        return Success($"# Document: {document.Path}\n\n{sectionContent}");
    }
    
    private ToolExecutionResult RepoFetchOffsets(EmdReferencePath refPath)
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

        if (length > maxChars)
        {
            return Error($"repo_fetch: size limit ({maxChars}) exceeded. Please fetch a smaller number of characters.");
        }

        var content = document.Content.Substring(refPath.StartOffset, length);

        return Success($"# Document: {document.Path}\n\n{content}");
    }
}