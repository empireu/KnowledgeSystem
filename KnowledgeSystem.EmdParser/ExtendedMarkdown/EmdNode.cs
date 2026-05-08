using KnowledgeSystem.EmdParser.MarkdownTree;

namespace KnowledgeSystem.EmdParser.ExtendedMarkdown;

public sealed class EmdNode(MarkdownNode rawNode)
{
    /// <summary>
    ///     Markdown node this data is attached to.
    /// </summary>
    public readonly MarkdownNode RawNode = rawNode;
    
    /// <summary>
    ///     The document containing this node.
    /// </summary>
    public EmdDocument Document { get; internal set; } = null!;

    /// <summary>
    ///     Raw user-defined definition ID.
    /// </summary>
    public string? Definition { get; internal set; }
    public EmdReferencePath? DefinitionPath { get; internal set; }
    
    /// <summary>
    ///     Raw user-defined dependency IDs.
    /// </summary>
    public readonly List<string> DeclaredDependencies = [];
    public readonly List<EmdReferencePath> DeclaredDependencyRefs = [];
    
    /// <summary>
    ///     Chunks representing this node.
    /// </summary>
    public readonly List<EmdChunk> Chunks = [];
}