using System.Security.Cryptography;
using System.Text;
using KnowledgeSystem.EmdParser.MarkdownTree;

// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.EmdParser.ExtendedMarkdown;

public readonly struct EmdChunkHash(byte[] hash, int preComputedHashCode) : IEquatable<EmdChunkHash>
{
    public readonly byte[] Hash = hash;

    public string ToHexString() => Convert.ToHexString(Hash);

    public static EmdChunkHash Compute(EmdChunk chunk)
    {
        var sb = new StringBuilder();
        
        // Identifies change of file name or path:
        sb.AppendLine(chunk.Node.Document.Path);
        
        // Identifies change of document structure, outside the content of the node:
        AppendHeadingPath(chunk.Node.RawNode, sb);
        
        // Identifies the position within the node (disambiguates sub-chunks of the same node):
        sb.Append(chunk.StartOffset).Append(' ');

        // Identifies changes in the content:
        sb.Append(chunk.ChunkText);
        
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        var hashCode = new HashCode();

        for (var index = 0; index < hashBytes.Length; index++)
        {
            hashCode.Add(hashBytes[index]);
        }
        
        return new EmdChunkHash(hashBytes, hashCode.ToHashCode());
    }

    private static void AppendHeadingPath(MarkdownNode node, StringBuilder builder)
    {
        if (node.Parent != null)
        {
            AppendHeadingPath(node.Parent, builder);
        }

        if (node.HeadingLevel > 0)
        {
            builder.Append('#').Append(node.HeadingLevel).Append(' ');
            builder.AppendLine(node.Text);
        }
        else if (node.NodeType == MarkdownNode.Type.CodeBlock)
        {
            builder.Append("```").Append(node.Language).AppendLine("```");
        }
    }
    
    public bool Equals(EmdChunkHash other)
    {
        return Hash.SequenceEqual(other.Hash);
    }

    public override bool Equals(object? obj)
    {
        return obj is EmdChunkHash other && Equals(other);
    }

    public override int GetHashCode()
    {
        return preComputedHashCode;
    }

    public static bool operator ==(EmdChunkHash left, EmdChunkHash right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EmdChunkHash left, EmdChunkHash right)
    {
        return !left.Equals(right);
    }
}