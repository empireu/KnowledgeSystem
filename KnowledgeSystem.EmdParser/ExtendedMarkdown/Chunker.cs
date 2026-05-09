using System.Text;
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
            else
            {
                var prefix = BuildContextPrefix(node);

                if (prefix.Length >= MaxChunkLength / 2) // Crazy if it does reach this
                {
                    prefix = string.Empty;
                }

                if (text.Length <= MaxChunkLength - prefix.Length)
                {
                    // If possible, insert the prefix:
                    node.Chunks.Add(
                        new EmdChunk(
                            node,
                            startOffset: 0,
                            length: text.Length, 
                            chunkText: 
                            prefix + text
                        )
                    );
                }
                else
                {
                    SplitNode(node, text, prefix);
                }
            }
        }

        foreach (var child in node.RawNode.Children)
        {
            GenerateChunks(node.Document.AttachedNodes[child]);
        }
    }
    
    private void SplitNode(EmdNode node, string text, string prefix)
    {
        var effectiveMax = MaxChunkLength - prefix.Length;
        
        var isCode = node.RawNode.NodeType == MarkdownNode.Type.CodeBlock;
        
        var positions = isCode 
            ? FindCodeSplitPoints(text)
            : FindTextSplitPoints(text);

        if (positions.Count == 0)
        {
            // No natural split points found:
            HardSplit(node, text, prefix);
            return;
        }

        var start = 0;

        foreach (var splitEnd in positions)
        {
            var chunkText = text[start..splitEnd];

            if (chunkText.Length > effectiveMax && start < splitEnd)
            {
                // Segment between two split points is still too long, hard-split:
                HardSplitRange(node, text, start, splitEnd, prefix);
            }
            else if (chunkText.Length > 0)
            {
                node.Chunks.Add(new EmdChunk(node, startOffset: start, length: chunkText.Length, chunkText: prefix + chunkText));
            }

            start = splitEnd;
        }

        // Remaining tail:
        if (start < text.Length)
        {
            var tail = text[start..];

            if (tail.Length > effectiveMax)
            {
                HardSplitRange(node, text, start, text.Length, prefix);
            }
            else if (tail.Length > 0)
            {
                node.Chunks.Add(new EmdChunk(node, startOffset: start, length: tail.Length, chunkText: prefix + tail));
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
    private void HardSplit(EmdNode node, string text, string prefix)
    {
        HardSplitRange(node, text, 0, text.Length, prefix);
    }

    private void HardSplitRange(EmdNode node, string text, int rangeStart, int rangeEnd, string prefix)
    {
        var effectiveMax = Math.Max(50, MaxChunkLength - prefix.Length);
        var start = rangeStart;

        while (start < rangeEnd)
        {
            var remaining = rangeEnd - start;
            var take = Math.Min(remaining, effectiveMax);
            var chunkText = text.Substring(start, take);
            node.Chunks.Add(new EmdChunk(node, startOffset: start, length: take, chunkText: prefix + chunkText));
            start += take;
        }
    }

    private static string BuildContextPrefix(EmdNode node)
    {
        var sb = new StringBuilder();

        // Document Path:
        if (!string.IsNullOrEmpty(node.Document.Path))
        {
            sb.AppendLine($"[Document: {node.Document.Path}]");
        }

        // Definitions and dependencies:
        var definitions = new List<string>();
        var dependencies = new List<string>();
        var currentNode = node.RawNode;
        while (currentNode != null)
        {
            if (node.Document != null && node.Document.AttachedNodes.TryGetValue(currentNode, out var currentEmd))
            {
                if (!string.IsNullOrEmpty(currentEmd.Definition))
                {
                    definitions.Add(currentEmd.Definition);
                }
                
                dependencies.AddRange(currentEmd.DeclaredDependencies);
            }
            
            currentNode = currentNode.Parent;
        }

        if (definitions.Count > 0)
        {
            sb.AppendLine($"[Definitions: {string.Join(", ", definitions.Distinct())}]");
        }
        
        if (dependencies.Count > 0)
        {
            sb.AppendLine($"[Dependencies: {string.Join(", ", dependencies.Distinct())}]");
        }

        // Heading path:
        var headings = new List<string>();
        currentNode = node.RawNode.Parent;
        while (currentNode != null)
        {
            if (currentNode.HeadingLevel > 0 && !string.IsNullOrWhiteSpace(currentNode.Text))
            {
                headings.Insert(0, currentNode.Text.Trim());
            }
            
            currentNode = currentNode.Parent;
        }

        if (headings.Count > 0)
        {
            sb.AppendLine($"[Path: {string.Join(" > ", headings)}]");
        }

        // Structural hint:
        switch (node.RawNode.NodeType)
        {
            case MarkdownNode.Type.CodeBlock:
                var language = string.IsNullOrEmpty(node.RawNode.Language) ? "code" : node.RawNode.Language;
                sb.AppendLine($"[Code Block ({language})]");
                break;
            case MarkdownNode.Type.ListItem:
                sb.AppendLine("[List Item]");
                break;
            case MarkdownNode.Type.Blockquote:
                sb.AppendLine("[Quote]");
                break;
            case MarkdownNode.Type.Invalid:
            case MarkdownNode.Type.Document:
            case MarkdownNode.Type.H1:
            case MarkdownNode.Type.H2:
            case MarkdownNode.Type.H3:
            case MarkdownNode.Type.H4:
            case MarkdownNode.Type.H5:
            case MarkdownNode.Type.H6:
            case MarkdownNode.Type.Paragraph:
            case MarkdownNode.Type.UnorderedList:
            case MarkdownNode.Type.OrderedList:
            case MarkdownNode.Type.ThematicBreak:
            default:
                // Ignored
                break;
        }

        if (sb.Length > 0)
        {
            // Empty line before actual content:
            sb.AppendLine();
        }

        return sb.ToString();
    }
}