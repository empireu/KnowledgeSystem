namespace KnowledgeSystem.EmdParser.MarkdownTree;

/// <summary>
///     Represents a node in the document's tree.
/// </summary>
public class MarkdownNode(MarkdownNode.Type type, int startOffset, int endOffset)
{
    public enum Type : byte
    {
        /// <summary>
        ///     Default value. Should never appear on a valid node.
        /// </summary>
        Invalid = 0,
        /// <summary>
        ///     Root node of a parsed Markdown document.
        /// </summary>
        Document,
        /// <summary>
        ///     ATX heading level 1. (<c># </c>).
        /// </summary>
        H1,
        /// <summary>
        ///     ATX heading level 2 (<c>## </c>).
        /// </summary>
        H2,
        /// <summary>
        ///     ATX heading level 3 (<c>### </c>).
        /// </summary>
        H3,
        /// <summary>
        ///     ATX heading level 4 (<c>#### </c>).
        /// </summary>
        H4,
        /// <summary>
        ///     ATX heading level 5 (<c>##### </c>).
        /// </summary>
        H5,
        /// <summary>
        ///     ATX heading level 6 (<c>###### </c>).
        /// </summary>
        H6,
        /// <summary>
        ///     One or more consecutive non-blank, non-structural lines of text.
        /// </summary>
        Paragraph,
        /// <summary>
        ///     Container for consecutive unordered list items (<c>-</c>, <c>*</c>, <c>+</c>).
        /// </summary>
        UnorderedList,
        /// <summary>
        ///     Container for consecutive ordered list items (<c>1.</c>, <c>2.</c>, etc.).
        /// </summary>
        OrderedList,
        /// <summary>
        ///     A single list item, child of <see cref="UnorderedList"/> or <see cref="OrderedList"/>.
        /// </summary>
        ListItem,
        /// <summary>
        ///     Fenced code block delimited by <c>```</c> or <c>~~~</c>.
        /// </summary>
        CodeBlock,
        /// <summary>
        ///     Blockquote section (<c>&gt;</c> prefix on one or more consecutive lines).
        /// </summary>
        Blockquote,
        /// <summary>
        ///     Thematic break / separator (<c>---</c>, <c>***</c>, <c>___</c>).
        /// </summary>
        ThematicBreak,
    }

    public readonly Type NodeType = type;
    
    /// <summary>
    ///     The parent of the node.
    ///     Null if the <see cref="Type"/> is a <see cref="Type.Document"/>.
    /// </summary>
    public MarkdownNode? Parent { get; internal set; }
    
    /// <summary>
    ///     The child nodes. Will be empty for logical content.
    /// </summary>
    public List<MarkdownNode> Children { get; } = [];

    /// <summary>
    ///     Start offset of this node's span in the input string (inclusive).
    /// </summary>
    public int StartOffset { get; internal set; } = startOffset;

    /// <summary>
    ///     End offset of this node's span in the input string (exclusive).
    /// </summary>
    public int EndOffset { get; internal set; } = endOffset;

    /// <summary>
    ///     Heading level. Range is 1 to 6 when the <see cref="Type"/> is an ATX heading and 0 for non-heading.
    /// </summary>
    public int HeadingLevel => NodeType switch
    {
        Type.H1 => 1, 
        Type.H2 => 2,
        Type.H3 => 3,
        Type.H4 => 4,
        Type.H5 => 5,
        Type.H6 => 6,
        _ => 0
    };

    /// <summary>
    ///     Extracted content text:
    ///     <list type="bullet">
    ///         <item><description>For headings, this will be the isolated text.</description></item>
    ///         <item><description>For paragraphs, quotes and list items, this is simply the text.</description></item>
    ///         <item><description>For code blocks, this is the code without the fences.</description></item>
    ///         <item><description>For everything else, this is empty.</description></item>
    ///     </list>
    /// </summary>
    public string Text { get; internal set; } = string.Empty;

    /// <summary>
    ///     The language identifier for <see cref="Type.CodeBlock"/> nodes (e.g. <c>cs</c>).
    ///     Empty for all other node types and for code blocks without a language identifier.
    /// </summary>
    public string Language { get; internal set; } = string.Empty;
}