using System.Text;

// ReSharper disable InlineTemporaryVariable
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.EmdParser.MarkdownTree;

public static partial class MarkdownTreeParser
{
    private static void AddChild(MarkdownNode parent, MarkdownNode child)
    {
        child.Parent = parent;
        parent.Children.Add(child);
    }

    private static void FixEndOffsets(MarkdownNode node)
    {
        if (node.Children.Count == 0)
        {
            return;
        }

        for (var index = 0; index < node.Children.Count; index++)
        {
            FixEndOffsets(node.Children[index]);
        }

        node.EndOffset = node.Children[^1].EndOffset;
    }

    private static int LineEnd(string input, int start)
    {
        var i = start;

        while (i < input.Length && input[i] != '\r' && input[i] != '\n')
        {
            i++;
        }

        return i;
    }

    private static int NextLineStart(string input, int lineEnd)
    {
        var i = lineEnd;

        if (i < input.Length && input[i] == '\r')
        {
            i++;
        }

        if (i < input.Length && input[i] == '\n')
        {
            i++;
        }

        return i;
    }

    private static bool IsBlankLine(string input, int lineStart, int lineEnd)
    {
        for (var i = lineStart; i < lineEnd; i++)
        {
            if (!char.IsWhiteSpace(input[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Counts virtual columns of leading whitespace (spaces and 4-space tabs) up to <paramref name="maxColumn"/>.
    ///     Tabs that overshoot <see cref="maxColumn"/> are still consumed since they cover the target column.
    /// </summary>
    private static int CountIndent(string input, int start, int end, int maxColumn, out int index)
    {
        var column = 0;
        var i = start;
        while (i < end)
        {
            if (input[i] == ' ')
            {
                if (column >= maxColumn)
                {
                    break;
                }

                column++;
                i++;
            }
            else if (input[i] == '\t')
            {
                if (column >= maxColumn)
                {
                    break;
                }

                var nextTabStop = (column + 4) & ~3;
                column = nextTabStop;
                i++;
            }
            else
            {
                break;
            }
        }

        index = i;
        return column;
    }

    /// <summary>
    ///     Skips characters contributing to indentation up to <paramref name="targetColumn"/> virtual columns.
    ///     Tabs that overshoot are still consumed since they cover the target.
    /// </summary>
    private static int SkipIndentChars(string input, int start, int end, int targetColumn)
    {
        var column = 0;
        var i = start;
        while (i < end && column < targetColumn)
        {
            if (input[i] == ' ')
            {
                column++;
                i++;
            }
            else if (input[i] == '\t')
            {
                var nextTabStop = (column + 4) & ~3;
                column = nextTabStop;
                i++;
            }
            else
            {
                break;
            }
        }

        return i;
    }

    private static bool IsUnorderedListItem(string input, int lineStart, int lineEnd, out int markerWidth, out int contentStart)
    {
        markerWidth = 0;
        contentStart = lineStart;
        var indent = CountIndent(input, lineStart, lineEnd, 3, out var i);

        if (i >= lineEnd)
        {
            return false;
        }

        var marker = input[i];
        if (marker != '-' && marker != '*' && marker != '+')
        {
            return false;
        }

        // Must be followed by at least one space:
        if (i + 1 >= lineEnd || input[i + 1] != ' ')
        {
            return false;
        }

        markerWidth = indent + 2;
        contentStart = i + 2;
        return true;
    }

    private static bool IsOrderedListItem(string input, int lineStart, int lineEnd, out int markerWidth, out int contentStart)
    {
        markerWidth = 0;
        contentStart = lineStart;
        var indent = CountIndent(input, lineStart, lineEnd, 3, out var i);

        if (i >= lineEnd || !char.IsDigit(input[i]))
        {
            return false;
        }

        // Count digits (1-9):
        var digits = 0;
        while (i < lineEnd && digits < 9 && char.IsDigit(input[i]))
        {
            digits++;
            i++;
        }

        if (i >= lineEnd)
        {
            return false;
        }

        // Must be followed by "." or ")", then a space:
        if (input[i] != '.' && input[i] != ')')
        {
            return false;
        }

        if (i + 1 >= lineEnd || input[i + 1] != ' ')
        {
            return false;
        }

        markerWidth = indent + digits + 2;
        contentStart = i + 2;
        return true;
    }

    private static string ExtractListItemText(string input, int itemStart, int itemEnd, int markerWidth, int firstContentStart)
    {
        // First line. contentStart is the character position after the marker:
        var firstLineEnd = LineEnd(input, itemStart);
        var contentStart = firstContentStart;
        if (contentStart > firstLineEnd)
        {
            contentStart = firstLineEnd;
        }

        var sb = new StringBuilder();
        sb.Append(input[contentStart..firstLineEnd]);

        // Continuation lines. Strip the markerWidth of leading indentation:
        var pos = NextLineStart(input, firstLineEnd);
        while (pos < itemEnd)
        {
            var continuationLineStart = pos;
            var continuationLineEnd = LineEnd(input, pos);

            sb.Append('\n');

            var k = SkipIndentChars(input, continuationLineStart, continuationLineEnd, markerWidth);
            sb.Append(input[k..continuationLineEnd]);

            pos = NextLineStart(input, continuationLineEnd);
        }

        return sb.ToString().Trim();
    }

    private static bool IsAtxHeading(string input, int lineStart, int lineEnd, out int level)
    {
        level = 0;
        CountIndent(input, lineStart, lineEnd, 3, out var i);

        var count = 0;
        while (i < lineEnd && count < 7 && input[i] == '#')
        {
            count++;
            i++;
        }

        if (count is < 1 or > 6)
        {
            return false;
        }

        // Must be followed by space, tab, or end-of-line:
        if (i < lineEnd && input[i] != ' ' && input[i] != '\t')
        {
            return false;
        }

        level = count;
        return true;
    }

    private static bool IsFencedCodeBlockStart(string input, int lineStart, int lineEnd, out int fenceLength)
    {
        fenceLength = 0;
        CountIndent(input, lineStart, lineEnd, 3, out var i);

        if (i >= lineEnd)
        {
            return false;
        }

        var fenceChar = input[i];
        if (fenceChar != '`' && fenceChar != '~')
        {
            return false;
        }

        var count = 0;
        while (i < lineEnd && input[i] == fenceChar)
        {
            count++;
            i++;
        }

        if (count < 3)
        {
            return false;
        }

        fenceLength = count;
        return true;
    }

    private static bool IsFencedCodeBlockEnd(string input, int lineStart, int lineEnd, int fenceLength)
    {
        CountIndent(input, lineStart, lineEnd, 3, out var i);

        if (i >= lineEnd)
        {
            return false;
        }

        var fenceChar = input[i];
        if (fenceChar != '`' && fenceChar != '~')
        {
            return false;
        }

        var count = 0;
        while (i < lineEnd && input[i] == fenceChar)
        {
            count++;
            i++;
        }

        if (count < fenceLength)
        {
            return false;
        }

        // Rest of line must be blank:
        while (i < lineEnd)
        {
            if (!char.IsWhiteSpace(input[i]))
            {
                return false;
            }

            i++;
        }

        return true;
    }

    private static bool IsBlockquoteLine(string input, int lineStart, int lineEnd)
    {
        CountIndent(input, lineStart, lineEnd, 3, out var i);
        return i < lineEnd && input[i] == '>';
    }

    private static string ExtractBlockquoteText(string input, int blockQuoteStart, int blockQuoteEnd)
    {
        var sb = new StringBuilder();
        var pos = blockQuoteStart;

        while (pos < blockQuoteEnd)
        {
            var lineStart = pos;
            var lineEnd = LineEnd(input, pos);

            // Skip indentation up to 3 cols:
            var contentPos = SkipIndentChars(input, lineStart, lineEnd, 3);

            // Skip the > character:
            if (contentPos < lineEnd && input[contentPos] == '>')
            {
                contentPos++;
            }

            // Skip one optional space after > (per CommonMark):
            if (contentPos < lineEnd && input[contentPos] == ' ')
            {
                contentPos++;
            }

            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(input[contentPos..lineEnd]);
            pos = NextLineStart(input, lineEnd);
        }

        return sb.ToString().Trim();
    }

    private static bool IsThematicBreak(string input, int lineStart, int lineEnd)
    {
        CountIndent(input, lineStart, lineEnd, 3, out var i);

        if (i >= lineEnd)
        {
            return false;
        }

        var breakChar = input[i];
        if (breakChar != '-' && breakChar != '*' && breakChar != '_')
        {
            return false;
        }

        var count = 0;
        while (i < lineEnd)
        {
            if (input[i] == breakChar)
            {
                count++;
            }
            else if (!char.IsWhiteSpace(input[i]))
            {
                return false;
            }

            i++;
        }

        return count >= 3;
    }

    private static string ExtractHeadingText(string input, int lineStart, int lineEnd, int level)
    {
        // Skip indentation:
        CountIndent(input, lineStart, lineEnd, int.MaxValue, out var i);

        // Skip heading characters:
        i += level;

        // Skip mandatory space/tab after #:
        if (i < lineEnd && (input[i] == ' ' || input[i] == '\t'))
        {
            i++;
        }

        // Strip optional closing # sequence at end of line
        var textEnd = lineEnd;
        var j = lineEnd - 1;
        while (j >= i && input[j] == '#')
        {
            j--;
        }

        // Closing #s must be preceded by space/tab:
        if (j >= i && (input[j] == ' ' || input[j] == '\t') && j + 1 < lineEnd && input[j + 1] == '#')
        {
            textEnd = j;
        }

        return input[i..textEnd].Trim();
    }

    private static string ExtractCodeLanguage(string input, int lineStart, int lineEnd)
    {
        CountIndent(input, lineStart, lineEnd, 3, out var i);

        // Skip fence characters
        var fenceChar = input[i];
        while (i < lineEnd && input[i] == fenceChar)
        {
            i++;
        }

        // Skip whitespace between fence and language identifier:
        while (i < lineEnd && char.IsWhiteSpace(input[i]))
        {
            i++;
        }

        // Read language identifier (non-whitespace characters):
        var languageStart = i;
        while (i < lineEnd && !char.IsWhiteSpace(input[i]))
        {
            i++;
        }

        return languageStart < i ? input[languageStart..i] : string.Empty;
    }

    private static string ExtractCodeText(string input, int codeStart, int codeEnd)
    {
        // Skip the opening fence line:
        var openLineEnd = LineEnd(input, codeStart);
        var contentStart = NextLineStart(input, openLineEnd);

        // Skip the closing fence line:

        // Walk back to find the start of the closing fence line:
        var k = codeEnd - 1;
        // Handle trailing newlines:
        while (k > contentStart && (input[k] == '\n' || input[k] == '\r'))
        {
            k--;
        }

        // Now k is at the end of the closing fence line. Walk back to find its start:
        while (k > contentStart && input[k] != '\n' && input[k] != '\r')
        {
            k--;
        }

        var contentEnd = k > contentStart
            ? k + 1
            : contentStart; // Past the newline

        return contentEnd <= contentStart
            ? string.Empty
            : input[contentStart..contentEnd].TrimEnd();
    }

    private static MarkdownNode.Type LevelToType(int level) => level switch
    {
        1 => MarkdownNode.Type.H1,
        2 => MarkdownNode.Type.H2,
        3 => MarkdownNode.Type.H3,
        4 => MarkdownNode.Type.H4,
        5 => MarkdownNode.Type.H5,
        6 => MarkdownNode.Type.H6,
        _ => MarkdownNode.Type.Invalid
    };
}