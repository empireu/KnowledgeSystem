// ReSharper disable InlineTemporaryVariable
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.EmdParser.MarkdownTree;

public static partial class MarkdownTreeParser
{
    /// <summary>
    ///     Parses a Markdown document into a tree of nodes.
    ///     This is a relatively crude system meant for extracting the structure of the document via its boundaries.
    /// </summary>
    public static MarkdownNode Parse(string input)
    {
        var root = new MarkdownNode(MarkdownNode.Type.Document, 0, input.Length);
        if (string.IsNullOrEmpty(input))
        {
            return root;
        }

        var stack = new Stack<MarkdownNode>();
        stack.Push(root);

        var i = 0;
        var length = input.Length;

        while (i < length)
        {
            var lineStart = i;
            var lineEnd = LineEnd(input, i);
            
            if (ParseCodeBlock(input, lineStart, lineEnd, length, stack, ref i))
            {
                continue;
            }

            if (ParseBlockquote(input, lineStart, lineEnd, length, stack, ref i))
            {
                continue;
            }

            if (ParseThematicBreak(input, lineStart, lineEnd, stack, ref i))
            {
                continue;
            }

            if (ParseAtxHeading(input, lineStart, lineEnd, stack, ref i))
            {
                continue;
            }

            if (IsBlankLine(input, lineStart, lineEnd))
            {
                i = NextLineStart(input, lineEnd);
                continue;
            }

            if (ParseListItem(input, lineStart, lineEnd, stack, length, ref i))
            {
                continue;
            }

            // Otherwise, collect consecutive lines as a paragraph:
            ParseParagraph(input, lineStart, ref i, length, stack);
        }

        // Fix up end offsets.
        // We can only determine how much parent nodes span after we parsed their entire tree:
        FixEndOffsets(root);

        return root;
    }

    /// <summary>
    ///     Attempts to parse a fenced code block starting at the current line.
    ///     If the line is a code block opening fence, consumes all lines until the closing fence or end of input and creates a <see cref="MarkdownNode.Type.CodeBlock"/> node.
    /// </summary>
    private static bool ParseCodeBlock(string input, int lineStart, int lineEnd, int length, Stack<MarkdownNode> stack, ref int i)
    {
        if (!IsFencedCodeBlockStart(input, lineStart, lineEnd, out var fenceLength))
        {
            return false;
        }
        
        var codeStart = lineStart;
        i = NextLineStart(input, lineEnd);

        while (i < length)
        {
            var blockLineStart = i;
            var blockLineEnd = LineEnd(input, i);
                    
            if (IsFencedCodeBlockEnd(input, blockLineStart, blockLineEnd, fenceLength))
            {
                i = NextLineStart(input, blockLineEnd);
                break;
            }
                    
            i = NextLineStart(input, blockLineEnd);
        }

        var codeBlock = new MarkdownNode(MarkdownNode.Type.CodeBlock, codeStart, i)
        {
            Text = ExtractCodeText(input, codeStart, i),
            Language = ExtractCodeLanguage(input, codeStart, lineEnd)
        };
                
        AddChild(stack.Peek(), codeBlock);
        return true;
    }
    
    /// <summary>
    ///     Attempts to parse a blockquote starting at the current line.
    ///     Consumes consecutive lines prefixed with <c>&gt;</c> and creates a <see cref="MarkdownNode.Type.Blockquote"/> node.
    /// </summary>
    private static bool ParseBlockquote(string input, int lineStart, int lineEnd, int length, Stack<MarkdownNode> stack, ref int i)
    {
        if (!IsBlockquoteLine(input, lineStart, lineEnd))
        {
            return false;
        }
        
        var blockQuoteStart = lineStart;
        i = NextLineStart(input, lineEnd);

        // Collect consecutive blockquote lines:
        while (i < length)
        {
            var blockQuoteLineStart = i;
            var blockQuoteLineEnd = LineEnd(input, i);

            if (IsBlankLine(input, blockQuoteLineStart, blockQuoteLineEnd))
            {
                break;
            }
                    
            if (!IsBlockquoteLine(input, blockQuoteLineStart, blockQuoteLineEnd))
            {
                break;
            }

            i = NextLineStart(input, blockQuoteLineEnd);
        }

        var blockquote = new MarkdownNode(MarkdownNode.Type.Blockquote, blockQuoteStart, i)
        {
            Text = ExtractBlockquoteText(input, blockQuoteStart, i)
        };
                
        AddChild(stack.Peek(), blockquote);
        return true;
    }
    
    /// <summary>
    ///     Attempts to parse a thematic break (separator) at the current line.
    ///     If recognized, creates a <see cref="MarkdownNode.Type.ThematicBreak"/> node.
    /// </summary>
    private static bool ParseThematicBreak(string input, int lineStart, int lineEnd, Stack<MarkdownNode> stack, ref int i)
    {
        if (!IsThematicBreak(input, lineStart, lineEnd))
        {
            return false;
        }
        
        var thematicBreak = new MarkdownNode(MarkdownNode.Type.ThematicBreak, lineStart, NextLineStart(input, lineEnd));
        AddChild(stack.Peek(), thematicBreak);
        i = NextLineStart(input, lineEnd);
        return true;
    }
    
    /// <summary>
    ///     Attempts to parse a heading (<c>#</c> through <c>######</c>) at the current line.
    ///     Adjusts the heading stack and creates the appropriate heading node.
    /// </summary>
    private static bool ParseAtxHeading(string input, int lineStart, int lineEnd, Stack<MarkdownNode> stack, ref int i)
    {
        if (!IsAtxHeading(input, lineStart, lineEnd, out int headingLevel))
        {
            return false;
        }
        
        while (stack.Count > 1 && stack.Peek().HeadingLevel >= headingLevel)
        {
            stack.Pop();
        }

        var heading = new MarkdownNode(LevelToType(headingLevel), lineStart, lineEnd)
        {
            Text = ExtractHeadingText(input, lineStart, lineEnd, headingLevel)
        };
                
        AddChild(stack.Peek(), heading);
        stack.Push(heading);
        i = NextLineStart(input, lineEnd);
        return true;
    }
    
    /// <summary>
    ///     Attempts to parse an unordered or ordered list item at the current line.
    ///     Appends to an existing list or creates a new one. Continuation lines indented by at least the marker width are included in the item.
    /// </summary>
    private static bool ParseListItem(string input, int lineStart, int lineEnd, Stack<MarkdownNode> stack, int length, ref int i)
    {
        // ReSharper disable InlineOutVariableDeclaration
        var orderedListMarkerWidth = 0;
        var orderedListContentStart = 0;
        int unorderedListMarkerWidth;
        int unorderedListContentStart;
        // ReSharper restore InlineOutVariableDeclaration
        if (!IsUnorderedListItem(input, lineStart, lineEnd, out unorderedListMarkerWidth, out unorderedListContentStart) &&
            !IsOrderedListItem(input, lineStart, lineEnd, out orderedListMarkerWidth, out orderedListContentStart))
        {
            return false;
        }
        
        var isOrdered = orderedListMarkerWidth > 0;
        var markerWidth = isOrdered ? orderedListMarkerWidth : unorderedListMarkerWidth;
        var contentStart = isOrdered ? orderedListContentStart : unorderedListContentStart;
        var listType = isOrdered ? MarkdownNode.Type.OrderedList : MarkdownNode.Type.UnorderedList;

        // Check if we are inside an existing list:
        MarkdownNode? listNode = null;
        var currentParent = stack.Peek();
        if (currentParent.Children.Count > 0 && currentParent.Children[^1].NodeType == listType)
        {
            listNode = currentParent.Children[^1];
        }

        if (listNode == null)
        {
            listNode = new MarkdownNode(listType, lineStart, lineStart);
            AddChild(stack.Peek(), listNode);
        }

        // Parse one list item, which can span multiple lines:
        var itemStart = lineStart;
        i = NextLineStart(input, lineEnd);

        // Continuation lines: indented by at least the marker width:
        while (i < length)
        {
            var continuationLineStart = i;
            var continuationLineEnd = LineEnd(input, i);

            if (IsBlankLine(input, continuationLineStart, continuationLineEnd))
            {
                break;
            }
                    
            if (IsAtxHeading(input, continuationLineStart, continuationLineEnd, out _))
            {
                break;
            }
                    
            if (IsFencedCodeBlockStart(input, continuationLineStart, continuationLineEnd, out _))
            {
                break;
            }
                    
            if (IsThematicBreak(input, continuationLineStart, continuationLineEnd))
            {
                break;
            }
                    
            if (IsBlockquoteLine(input, continuationLineStart, continuationLineEnd))
            {
                break;
            }
                    
            if (IsUnorderedListItem(input, continuationLineStart, continuationLineEnd, out _, out _) || IsOrderedListItem(input, continuationLineStart, continuationLineEnd, out _, out _))
            {
                break;
            }

            // Check indentation:
            var indent = CountIndent(input, continuationLineStart, continuationLineEnd, markerWidth, out _);
                    
            if (indent < markerWidth)
            {
                break;
            }

            i = NextLineStart(input, continuationLineEnd);
        }

        var listItem = new MarkdownNode(MarkdownNode.Type.ListItem, itemStart, i)
        {
            Text = ExtractListItemText(input, itemStart, i, markerWidth, contentStart)
        };
                
        AddChild(listNode, listItem);
        listNode.EndOffset = i;
        return true;
    }
    
    /// <summary>
    ///     Collects consecutive non-structural, non-blank lines into a <see cref="MarkdownNode.Type.Paragraph"/> node.
    ///     Paragraph collection stops at blank lines or any recognized block-level construct.
    /// </summary>
    private static void ParseParagraph(string input, int lineStart, ref int i, int length, Stack<MarkdownNode> stack)
    {
        var paragraphStart = lineStart;
        while (i < length)
        {
            var paragraphLineStart = i;
            var paragraphLineEnd = LineEnd(input, i);

            if (IsBlankLine(input, paragraphLineStart, paragraphLineEnd))
            {
                break;
            }
                    
            if (IsAtxHeading(input, paragraphLineStart, paragraphLineEnd, out _))
            {
                break;
            }
                    
            if (IsFencedCodeBlockStart(input, paragraphLineStart, paragraphLineEnd, out _))
            {
                break;
            }
                    
            if (IsThematicBreak(input, paragraphLineStart, paragraphLineEnd))
            {
                break;
            }
                    
            if (IsBlockquoteLine(input, paragraphLineStart, paragraphLineEnd))
            {
                break;
            }
                    
            if (IsUnorderedListItem(input, paragraphLineStart, paragraphLineEnd, out _, out _) || IsOrderedListItem(input, paragraphLineStart, paragraphLineEnd, out _, out _))
            {
                break;
            }

            i = NextLineStart(input, paragraphLineEnd);
        }

        var paragraph = new MarkdownNode(MarkdownNode.Type.Paragraph, paragraphStart, i)
        {
            Text = input[paragraphStart..i].TrimEnd()
        };
                
        AddChild(stack.Peek(), paragraph);
    }
}