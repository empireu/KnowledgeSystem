using KnowledgeSystem.EmdParser.MarkdownTree;

namespace KnowledgeSystem.EmdParser.ExtendedMarkdown;

/// <summary>
///     Utility for generating chunks centered around a Markdown node.
/// </summary>
public sealed class Chunker(int maxChunkLength)
{
    /// <summary>
    ///     Maximum character length for a single chunk.
    ///     Nodes exceeding this will be split.
    /// </summary>
    public readonly int MaxChunkLength = maxChunkLength;

    private static bool IsChunkable(MarkdownNode node)
    {
        return node.NodeType is MarkdownNode.Type.Paragraph
            or MarkdownNode.Type.CodeBlock
            or MarkdownNode.Type.ListItem
            or MarkdownNode.Type.Blockquote;
    }
    
    /// <summary>
    ///     Generates chunks for the given node and all its descendants, appending them to <see cref="EmdNode.Chunks"/>.
    /// </summary>
    public void GenerateChunks(EmdNode node)
    {
        // Only leaf nodes with content produce chunks:
        if (IsChunkable(node.RawNode))
        {
            var text = node.RawNode.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                // Should not happen, but skip
            }
            else if (text.Length <= MaxChunkLength)
            {
                // TODO enrich
                node.Chunks.Add(new EmdChunk(node, startOffset: 0, length: text.Length, chunkText: text));
            }
            else
            {
                SplitNode(node, text);
            }
        }

        foreach (var child in node.RawNode.Children)
        {
            GenerateChunks(node.Document.AttachedNodes[child]);
        }
    }
    
    private void SplitNode(EmdNode node, string text)
    {
        var isCode = node.RawNode.NodeType == MarkdownNode.Type.CodeBlock;
        var positions = isCode 
            ? FindCodeSplitPoints(text)
            : FindTextSplitPoints(text);

        if (positions.Count == 0)
        {
            // No natural split points found:
            HardSplit(node, text);
            return;
        }

        var start = 0;

        foreach (var splitEnd in positions)
        {
            var chunkText = text[start..splitEnd];

            if (chunkText.Length > MaxChunkLength && start < splitEnd)
            {
                // Segment between two split points is still too long, hard-split:
                HardSplitRange(node, text, start, splitEnd);
            }
            else if (chunkText.Length > 0)
            {
                node.Chunks.Add(new EmdChunk(node, startOffset: start, length: chunkText.Length, chunkText: chunkText));
            }

            start = splitEnd;
        }

        // Remaining tail:
        if (start < text.Length)
        {
            var tail = text[start..];

            if (tail.Length > MaxChunkLength)
            {
                HardSplitRange(node, text, start, text.Length);
            }
            else if (tail.Length > 0)
            {
                node.Chunks.Add(new EmdChunk(node, startOffset: start, length: tail.Length, chunkText: tail));
            }
        }
    }

    /// <summary>
    ///     Finds split points for text content at sentence boundaries.
    ///     Returns indices just after the boundary.
    /// </summary>
    private static List<int> FindTextSplitPoints(string text)
    {
        var points = new List<int>();
        var i = 0;

        while (i < text.Length)
        {
            var newline = text.IndexOf('\n', i);
            if (newline >= 0)
            {
                points.Add(newline + 1);
                i = newline + 1;
                continue;
            }

            // Sentence endings: ". ", "! ", "? "
            var sentenceEnd = FindSentenceEnd(text, i);
            if (sentenceEnd >= 0)
            {
                points.Add(sentenceEnd);
                i = sentenceEnd;
            }
            else
            {
                break;
            }
        }

        return points;
    }

    /// <summary>
    ///     Finds split points for code content at newlines only.
    /// </summary>
    private static List<int> FindCodeSplitPoints(string text)
    {
        var points = new List<int>();
        var i = 0;

        while (i < text.Length)
        {
            var newline = text.IndexOf('\n', i);
            if (newline < 0)
            {
                break;
            }
            
            points.Add(newline + 1);
            i = newline + 1;
        }

        return points;
    }

    private static int FindSentenceEnd(string text, int start)
    {
        for (var i = start; i < text.Length - 1; i++)
        {
            if (text[i] is '.' or '!' or '?')
            {
                if (text[i + 1] == ' ' || text[i + 1] == '\n')
                {
                    return i + (text[i + 1] == ' ' ? 2 : 1);
                }
            }
        }

        return -1;
    }

    /// <summary>
    ///     Hard-splits text that has no natural boundaries.
    /// </summary>
    private void HardSplit(EmdNode node, string text)
    {
        HardSplitRange(node, text, 0, text.Length);
    }

    private void HardSplitRange(EmdNode node, string text, int rangeStart, int rangeEnd)
    {
        var start = rangeStart;

        while (start < rangeEnd)
        {
            var remaining = rangeEnd - start;
            var take = Math.Min(remaining, MaxChunkLength);
            var chunkText = text.Substring(start, take);
            node.Chunks.Add(new EmdChunk(node, startOffset: start, length: take, chunkText: chunkText));
            start += take;
        }
    }
}