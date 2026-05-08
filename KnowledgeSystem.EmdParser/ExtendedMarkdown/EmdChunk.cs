namespace KnowledgeSystem.EmdParser.ExtendedMarkdown;

public sealed class EmdChunk(EmdNode node, int startOffset, int length, string chunkText) : IEquatable<EmdChunk>
{
    /// <summary>
    ///     The EMD node containing this chunk.
    /// </summary>
    public readonly EmdNode Node = node;
    
    /// <summary>
    ///     The start offset in the node's <see cref="MarkdownTree.MarkdownNode.Text"/>
    /// </summary>
    public readonly int StartOffset = startOffset;
    
    /// <summary>
    ///     The length of the content, after chunking.
    /// </summary>
    public readonly int Length = length;
    
    /// <summary>
    ///     The extracted text.
    /// </summary>
    public readonly string ChunkText = chunkText;
    
    /// <summary>
    ///     The hash, computed for this chunk. It takes into account everything, including the on-disk path.
    ///     Set after the pointer tree has been initialized.
    /// </summary>
    public EmdChunkHash Hash { get; internal set; }

    public bool Equals(EmdChunk? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Node.Equals(other.Node) && Hash.Equals(other.Hash);
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is EmdChunk other && Equals(other);
    }

    public override int GetHashCode()
    {
        // Set after the tree is initialized.
        // ReSharper disable once NonReadonlyMemberInGetHashCode
        return HashCode.Combine(Node, Hash);
    }

    public static bool operator ==(EmdChunk? left, EmdChunk? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(EmdChunk? left, EmdChunk? right)
    {
        return !Equals(left, right);
    }
}