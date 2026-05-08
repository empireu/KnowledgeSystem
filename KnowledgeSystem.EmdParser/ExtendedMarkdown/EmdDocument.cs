using KnowledgeSystem.EmdParser.MarkdownTree;

// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.EmdParser.ExtendedMarkdown;

public sealed class EmdDocument
{
    public readonly EmdRepository Repository;
    public readonly string Path;
    public readonly string Content;
    public readonly EmdNode RootNode;
    public readonly Dictionary<MarkdownNode, EmdNode> AttachedNodes;
    public readonly Dictionary<EmdReferencePath, EmdNode> NodesWithDefinition;

    private EmdDocument(
        EmdRepository repository,
        string path,
        string content,
        EmdNode rootNode,
        Dictionary<MarkdownNode, EmdNode> attachedNodes, 
        Dictionary<EmdReferencePath, EmdNode> nodesWithDefinition)
    {
        Repository = repository;
        Path = path;
        Content = content;
        RootNode = rootNode;
        AttachedNodes = attachedNodes;
        NodesWithDefinition = nodesWithDefinition;
    }

    private sealed class ParseData(EmdRepository repository, string documentPath)
    {
        public readonly EmdRepository Repository = repository;
        public readonly string DocumentPath = documentPath;
        public readonly Dictionary<MarkdownNode, EmdNode> Attachments = new();
        public readonly Dictionary<string, EmdNode> LocalRefs = new();
        public readonly Dictionary<EmdReferencePath, EmdNode> Refs = new();
    }
    
    /// <summary>
    ///     Loads the extended Markdown document from the specified Markdown file.
    ///     The references will not be checked or resolved.
    /// </summary>
    /// <returns>The fully parsed EMD document.</returns>
    public static EmdDocument Load(EmdRepository repository, string path, string content)
    {
        var root = MarkdownTreeParser.Parse(content);
        
        var data = new ParseData(repository, path);
        
        InitializeTreeAndParseTags(root, data);
        ParseRefs(data);

        var document = new EmdDocument(
            repository,
            path, content,
            data.Attachments[root],
            data.Attachments, data.Refs
        );

        // Attach document to nodes:
        foreach (var node in data.Attachments.Values)
        {
            node.Document = document;
        }

        return document;
    }

    /// <summary>
    ///     Generates chunks for all nodes and computes their hashes.
    ///     Must be called after the document is created and nodes have their <see cref="EmdNode.Document"/> set.
    /// </summary>
    public void GenerateChunksAndHashes(Chunker chunker)
    {
        chunker.GenerateChunks(RootNode);
        ComputeHashes();
    }

    private void ComputeHashes()
    {
        foreach (var node in AttachedNodes.Values)
        {
            for (var chunkIndex = 0; chunkIndex < node.Chunks.Count; chunkIndex++)
            {
                var nodeChunk = node.Chunks[chunkIndex];
                nodeChunk.Hash = EmdChunkHash.Compute(nodeChunk);
            }
        }
    }

    /// <summary>
    ///     Traverses the raw Markdown tree, parsing the raw EMD data.
    /// </summary>
    private static void InitializeTreeAndParseTags(MarkdownNode node, ParseData data)
    {
        var attachment = new EmdNode(node);
        data.Attachments.Add(node, attachment);
        
        // Parses the EMD tags:
        const string definitionTag = "def";
        const string dependencyTag = "dependsOn";

        var hasDefinition = false;
        foreach (var definition in ParseEmdTags(node.Text, definitionTag))
        {
            if (string.IsNullOrEmpty(definition))
            {
                throw new Exception($"Malformed definition in {node.Text} for document {data.DocumentPath}");
            }
            
            if (hasDefinition)
            {
                throw new Exception($"Duplicate definition {definition} in block {node.Text} for document {data.DocumentPath}");
            }
            
            attachment.Definition = definition;
            hasDefinition = true;

            if (!data.LocalRefs.TryAdd(definition, attachment))
            {
                throw new Exception($"Duplicate definition {definition} in block {node.Text} for document {data.DocumentPath}");
            }
        }

        foreach (var dependency in ParseEmdTags(node.Text, dependencyTag))
        {
            if (string.IsNullOrEmpty(dependency))
            {
                throw new Exception($"Malformed dependency in {node.Text} for document {data.DocumentPath}");
            }

            if (attachment.DeclaredDependencies.Contains(dependency))
            {
                throw new Exception($"Duplicate dependency {dependency} in block {node.Text} for document {data.DocumentPath}");
            }
            
            attachment.DeclaredDependencies.Add(dependency);
        }

        // Traverse down:
        foreach (var child in node.Children)
        {
            InitializeTreeAndParseTags(child, data);
        }
    }

    private static IEnumerable<string> ParseEmdTags(string text, string tag)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(tag))
        {
            yield break;
        }

        var searchTarget = $"[{tag}";
        var currentIndex = 0;

        while (currentIndex < text.Length)
        {
            var startIndex = text.IndexOf(searchTarget, currentIndex, StringComparison.Ordinal);
            
            if (startIndex == -1)
            {
                break;
            }

            var afterTagIndex = startIndex + searchTarget.Length;

            if (afterTagIndex >= text.Length)
            {
                yield return string.Empty;
                break;
            }

            var nextChar = text[afterTagIndex];
            
            if (nextChar != ' ' && nextChar != ']')
            {
                currentIndex = afterTagIndex;
                continue;
            }

            int contentStart = afterTagIndex;

            if (nextChar == ' ')
            {
                contentStart++;
            }

            var closeIndex = text.IndexOf(']', contentStart);

            if (closeIndex == -1)
            {
                yield return text[contentStart..];
                break;
            }

            yield return text.Substring(contentStart, closeIndex - contentStart);
            currentIndex = closeIndex + 1;
        }
    }
    
    /// <summary>
    ///     Parses each raw reference string into an <see cref="EmdReferencePath"/>.
    /// </summary>
    private static void ParseRefs(ParseData data)
    {
        // Parse definition:
        foreach (var attachment in data.Attachments.Values)
        {
            if (attachment.Definition == null)
            {
                continue;
            }

            attachment.DefinitionPath = new EmdReferencePath(
                data.DocumentPath,
                EmdReferencePath.ReferenceType.Definition,
                attachment.Definition,
                startOffset: 0,
                endOffset: 0
            );

            data.Refs.Add(attachment.DefinitionPath.Value, attachment);
        }

        // Parse declared dependencies:
        var docDir = System.IO.Path.GetDirectoryName(data.DocumentPath) ?? string.Empty;

        foreach (var attachment in data.Attachments.Values)
        {
            for (var depIndex = 0; depIndex < attachment.DeclaredDependencies.Count; depIndex++)
            {
                var localDependency = attachment.DeclaredDependencies[depIndex];
                EmdReferencePath refPath;

                if (localDependency.StartsWith('@'))
                {
                    // Local reference:
                    var definitionName = localDependency[1..];

                    if (string.IsNullOrEmpty(definitionName))
                    {
                        throw new FormatException($"Malformed local dependency {localDependency} in document {data.DocumentPath}");
                    }

                    if (!data.LocalRefs.ContainsKey(definitionName))
                    {
                        throw new KeyNotFoundException($"Unknown local reference {localDependency} in document {data.DocumentPath}");
                    }

                    refPath = new EmdReferencePath(
                        data.DocumentPath,
                        EmdReferencePath.ReferenceType.Definition,
                        definitionName,
                        startOffset: 0,
                        endOffset: 0
                    );
                }
                else
                {
                    // External reference: parse the format, then resolve relative paths:
                    if (!EmdReferencePath.TryParse(localDependency, out refPath))
                    {
                        throw new FormatException($"Invalid dependency reference '{localDependency}' in document {data.DocumentPath}");
                    }

                    // Resolve relative paths against the document's directory within the repo:
                    var resolvedPath = ResolveRelativePath(data.Repository.RootDirectory, docDir, refPath.RepositoryRelativePath);

                    refPath = new EmdReferencePath(
                        resolvedPath,
                        refPath.Type,
                        refPath.Type == EmdReferencePath.ReferenceType.Definition ? refPath.Definition : string.Empty,
                        refPath.Type == EmdReferencePath.ReferenceType.Offsets ? refPath.StartOffset : 0,
                        refPath.Type == EmdReferencePath.ReferenceType.Offsets ? refPath.EndOffset : 0
                    );
                }

                attachment.DeclaredDependencyRefs.Add(refPath);
            }
        }
    }

    /// <summary>
    ///     Resolves a potentially relative path against the document's directory, returning a normalized repository-relative path.
    /// </summary>
    private static string ResolveRelativePath(string repoRoot, string documentDir, string path)
    {
        // Already absolute within the repo (starts with '/' or drive letter).
        if (path.StartsWith('/') || path.StartsWith('\\') || path.Length >= 2 && path[1] == ':')
        {
            return EmdRepository.NormalizePath(path);
        }

        // Combine document directory with the relative path, then resolve to repository-relative.
        var combined = System.IO.Path.Combine(documentDir, path);
        var absolute = System.IO.Path.GetFullPath(System.IO.Path.Combine(repoRoot, combined));

        return EmdRepository.GetRepositoryRelativePath(repoRoot, absolute);
    }

}