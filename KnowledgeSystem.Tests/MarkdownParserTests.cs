using System.Text;
using KnowledgeSystem.EmdParser.MarkdownTree;

namespace KnowledgeSystem.Tests;

public class MarkdownParserTests
{
    private const int Seed = 3141;

    #region Random Generator

    private enum BlockKind : byte
    {
        H1, H2, H3, H4, H5, H6,
        Paragraph,
        UnorderedList,
        OrderedList,
        CodeBlock,
        ThematicBreak
    }

    private static readonly string[] Words =
    [
        "Dotnet", ".NET", "Satoru", "Gojo", "Ryomen", "Sukuna", "RyuJIT", "Markdown-Esque",
        "AV1", "H.265", "AMD", "Intel", "NVidia", "Electrical-Age2", "google.com", "CI/CD"
    ];

    private static string RandomWord(Random random) => Words[random.Next(Words.Length)];

    private static string RandomLine(Random random, int minWords = 3, int maxWords = 10)
    {
        var count = random.Next(minWords, maxWords + 1);
        var sb = new StringBuilder();
        
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }
            
            sb.Append(RandomWord(random));
        }
        
        return sb.ToString();
    }

    private static string GenerateHeading(Random random, int level)
    {
        var prefix = new string('#', level);
        return $"{prefix} {RandomLine(random, 2, 5)}";
    }

    private static string GenerateParagraph(Random random)
    {
        var lines = random.Next(1, 4);
        var sb = new StringBuilder();
        
        for (var i = 0; i < lines; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }
            
            sb.Append(RandomLine(random, 4, 12));
        }
        
        return sb.ToString();
    }

    private static string GenerateUnorderedList(Random random)
    {
        var items = random.Next(2, 6);
        char[] markers = ['-', '*', '+'];
        var marker = markers[random.Next(markers.Length)];
        var sb = new StringBuilder();
        
        for (var i = 0; i < items; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }
            
            sb.Append($"{marker} {RandomLine(random, 2, 8)}");
            
            if (random.NextDouble() < 0.3)
            {
                sb.Append($"\n  {RandomLine(random, 3, 7)}");
            }
        }
        
        return sb.ToString();
    }

    private static string GenerateOrderedList(Random random)
    {
        var items = random.Next(2, 6);
        var sb = new StringBuilder();
        for (var i = 0; i < items; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }
            
            sb.Append($"{i + 1}. {RandomLine(random, 2, 8)}");

            if (random.NextDouble() < 0.3)
            {
                sb.Append($"\n   {RandomLine(random, 3, 7)}");
            }
        }
      
        return sb.ToString();
    }

    private static string GenerateCodeBlock(Random random)
    {
        var lines = random.Next(1, 8);
        var fenceChar = random.NextDouble() < 0.5 ? '`' : '~';
        var fenceLength = random.Next(3, 6);
        var fence = new string(fenceChar, fenceLength);
        var sb = new StringBuilder();
        
        sb.AppendLine(fence);
        
        for (var i = 0; i < lines; i++)
        {
            sb.AppendLine($"  var x{i} = {random.Next(100)};");
        }
        
        sb.Append(fence);
        return sb.ToString();
    }

    private static string GenerateThematicBreak(Random random)
    {
        char[] chars = ['-', '*', '_'];
        var c = chars[random.Next(chars.Length)];
        var length = random.Next(3, 8);
        return new string(c, length);
    }

    private static string GenerateDocument(Random random, int blockCount)
    {
        var sb = new StringBuilder();
        var types = Enum.GetValues<BlockKind>();

        for (var b = 0; b < blockCount; b++)
        {
            if (b > 0)
            {
                sb.Append("\n\n");
            }

            var kind = types[random.Next(types.Length)];
            sb.Append(kind switch
            {
                BlockKind.H1 => GenerateHeading(random, 1),
                BlockKind.H2 => GenerateHeading(random, 2),
                BlockKind.H3 => GenerateHeading(random, 3),
                BlockKind.H4 => GenerateHeading(random, 4),
                BlockKind.H5 => GenerateHeading(random, 5),
                BlockKind.H6 => GenerateHeading(random, 6),
                BlockKind.Paragraph => GenerateParagraph(random),
                BlockKind.UnorderedList => GenerateUnorderedList(random),
                BlockKind.OrderedList => GenerateOrderedList(random),
                BlockKind.CodeBlock => GenerateCodeBlock(random),
                BlockKind.ThematicBreak => GenerateThematicBreak(random),
                _ => RandomLine(random)
            });
        }

        return sb.ToString();
    }

    #endregion
    
    #region Helpers

    private static List<MarkdownNode> CollectNodes(MarkdownNode root, MarkdownNode.Type type)
    {
        var result = new List<MarkdownNode>();
        Traverse(root);
        return result;

        void Traverse(MarkdownNode n)
        {
            if (n.NodeType == type)
            {
                result.Add(n);
            }
            
            foreach (var c in n.Children)
            {
                Traverse(c);
            }
        }
    }

    private static HashSet<MarkdownNode.Type> CollectPresentTypes(MarkdownNode root)
    {
        var types = new HashSet<MarkdownNode.Type>();
        Traverse(root);
        return types;

        void Traverse(MarkdownNode n)
        {
            types.Add(n.NodeType);
            
            foreach (var c in n.Children)
            {
                Traverse(c);
            }
        }
    }

    private static void AssertParentChildConsistency(MarkdownNode node, string context)
    {
        foreach (var child in node.Children)
        {
            Assert.Same(node, child.Parent);
            AssertParentChildConsistency(child, context);
        }
    }

    private static void AssertOffsetsWithinParent(MarkdownNode node, string input, string context)
    {
        if (node.Parent != null)
        {
            Assert.True(
                node.StartOffset >= node.Parent.StartOffset, 
                $"{context}: " +
                $"Node {node.NodeType} start: {node.StartOffset} < " +
                $"parent {node.Parent.NodeType} start: {node.Parent.StartOffset}"
            );
            
            Assert.True(
                node.EndOffset <= node.Parent.EndOffset, 
                $"{context}: " +
                $"Node {node.NodeType} end: {node.EndOffset} > " +
                $"parent :{node.Parent.NodeType} end: {node.Parent.EndOffset}"
            );
        }

        foreach (var child in node.Children)
        {
            AssertOffsetsWithinParent(child, input, context);
        }
    }

    private static void AssertChildrenCoverParent(MarkdownNode node, string context)
    {
        if (node.Children.Count > 0)
        {
            // Last child's end must match parent's end:
            Assert.Equal(node.Children[^1].EndOffset, node.EndOffset);

            // First child must start at or after parent's start (headings start at the # line, children come after):
            Assert.True(
                node.Children[0].StartOffset >= node.StartOffset,
                $"{context}: first child: {node.Children[0].NodeType} start: {node.Children[0].StartOffset} < " +
                $"parent: {node.NodeType} start: {node.StartOffset}"
            );

            // Children should be in order with no overlaps:
            if (node.NodeType is not MarkdownNode.Type.Document
                and not MarkdownNode.Type.H1 and not MarkdownNode.Type.H2
                and not MarkdownNode.Type.H3 and not MarkdownNode.Type.H4
                and not MarkdownNode.Type.H5 and not MarkdownNode.Type.H6)
            {
                for (var i = 1; i < node.Children.Count; i++)
                {
                    Assert.True(
                        node.Children[i].StartOffset >= node.Children[i - 1].EndOffset, 
                        $"{context}: overlapping children in {node.NodeType} " +
                        $"at offsets {node.Children[i - 1].EndOffset}..{node.Children[i].StartOffset}"
                    );
                }
            }
        }

        foreach (var child in node.Children)
        {
            AssertChildrenCoverParent(child, context);
        }
    }

    private static void AssertHeadingHierarchy(MarkdownNode node, string context)
    {
        if (node.HeadingLevel > 0)
        {
            foreach (var child in node.Children.Where(child => child.HeadingLevel > 0))
            {
                Assert.True(
                    child.HeadingLevel > node.HeadingLevel, 
                    $"{context}: heading {child.HeadingLevel} found as child of heading {node.HeadingLevel}"
                );
            }
        }

        foreach (var child in node.Children)
        {
            AssertHeadingHierarchy(child, context);
        }
    }

    private static void AssertListStructure(MarkdownNode node, string context)
    {
        switch (node.NodeType)
        {
            case MarkdownNode.Type.UnorderedList or MarkdownNode.Type.OrderedList:
            {
                foreach (var child in node.Children)
                {
                    Assert.Equal(MarkdownNode.Type.ListItem, child.NodeType);
                    Assert.Same(node, child.Parent);
                }

                break;
            }
            case MarkdownNode.Type.ListItem:
                Assert.NotNull(node.Parent);
                Assert.True(
                    node.Parent!.NodeType is MarkdownNode.Type.UnorderedList or MarkdownNode.Type.OrderedList,
                    $"{context}: ListItem parent is {node.Parent.NodeType}, expected a list type"
                );
                break;
        }

        foreach (var child in node.Children)
        {
            AssertListStructure(child, context);
        }
    }

    private static void AssertTextMatchesInput(MarkdownNode node, string input, string context)
    {
        switch (node.NodeType)
        {
            case MarkdownNode.Type.Paragraph:
            {
                // Paragraph text should be a trimmed version of the input in the offset range:
                var raw = input[node.StartOffset..node.EndOffset];
                Assert.Equal(raw.TrimEnd(), node.Text);
                break;
            }
            case MarkdownNode.Type.H1 or MarkdownNode.Type.H2 or MarkdownNode.Type.H3 or MarkdownNode.Type.H4 or MarkdownNode.Type.H5 or MarkdownNode.Type.H6:
            {
                // Heading text should not contain # characters at the start:
                Assert.DoesNotMatch(@"^#+\s", node.Text);
                Assert.NotEqual(string.Empty, node.Text);
                break;
            }
            case MarkdownNode.Type.CodeBlock:
            {
                // Code block text should not contain fence markers at start/end:
                Assert.DoesNotMatch("^```", node.Text);
                Assert.DoesNotMatch("^~~~", node.Text);
                Assert.DoesNotMatch("```$", node.Text);
                Assert.DoesNotMatch("~~~$", node.Text);
                break;
            }
            case MarkdownNode.Type.ThematicBreak:
            case MarkdownNode.Type.Document:
            {
                Assert.Equal(string.Empty, node.Text);
                break;
            }
        }

        foreach (var child in node.Children)
        {
            AssertTextMatchesInput(child, input, context);
        }
    }

    private static void AssertAllInvariants(MarkdownNode root, string input, string context)
    {
        Assert.NotNull(root);
        Assert.Equal(MarkdownNode.Type.Document, root.NodeType);
        Assert.Equal(0, root.StartOffset);
        Assert.Equal(input.Length, root.EndOffset);
        Assert.Null(root.Parent);

        AssertParentChildConsistency(root, context);
        AssertOffsetsWithinParent(root, input, context);
        AssertChildrenCoverParent(root, context);
        AssertHeadingHierarchy(root, context);
        AssertListStructure(root, context);
        AssertTextMatchesInput(root, input, context);
    }
    
    #endregion

    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyDocument()
    {
        var root = MarkdownTreeParser.Parse("");

        Assert.Equal(MarkdownNode.Type.Document, root.NodeType);
        Assert.Equal(0, root.StartOffset);
        Assert.Equal(0, root.EndOffset);
        Assert.Empty(root.Children);
    }

    [Fact]
    public void Parse_OnlyBlankLines_ReturnsEmptyDocument()
    {
        var root = MarkdownTreeParser.Parse("   \n\n  \r\n\n");

        Assert.Equal(MarkdownNode.Type.Document, root.NodeType);
        Assert.Empty(root.Children);
    }

    [Theory]
    [InlineData(50, Seed)]
    [InlineData(100, Seed + 1)]
    [InlineData(200, Seed + 2)]
    public void Parse_RandomDocument_AllInvariantsHold(int blockCount, int seed)
    {
        var random = new Random(seed);
        var input = GenerateDocument(random, blockCount);
        var root = MarkdownTreeParser.Parse(input);

        AssertAllInvariants(root, input, $"Seed: {seed} Blocks: {blockCount}");
    }

    [Theory]
    [InlineData(200, Seed)]
    [InlineData(300, Seed + 10)]
    public void Parse_LargeRandomDocument_ContainsAllNodeTypes(int blockCount, int seed)
    {
        var random = new Random(seed);
        var input = GenerateDocument(random, blockCount);
        var root = MarkdownTreeParser.Parse(input);

        var present = CollectPresentTypes(root);

        Assert.Contains(MarkdownNode.Type.H1, present);
        Assert.Contains(MarkdownNode.Type.H2, present);
        Assert.Contains(MarkdownNode.Type.H3, present);
        Assert.Contains(MarkdownNode.Type.Paragraph, present);
        Assert.Contains(MarkdownNode.Type.UnorderedList, present);
        Assert.Contains(MarkdownNode.Type.OrderedList, present);
        Assert.Contains(MarkdownNode.Type.ListItem, present);
        Assert.Contains(MarkdownNode.Type.CodeBlock, present);
        Assert.Contains(MarkdownNode.Type.ThematicBreak, present);
    }

    [Fact]
    public void Parse_HeadingText_StripsHashPrefixAndOptionalClosingHashes()
    {
        var root = MarkdownTreeParser.Parse("# Abcd Efgh\n## IJK ##\n### lmn ###");
        var headings = CollectNodes(root, MarkdownNode.Type.H1)
            .Concat(CollectNodes(root, MarkdownNode.Type.H2))
            .Concat(CollectNodes(root, MarkdownNode.Type.H3))
            .ToList();

        Assert.Equal(3, headings.Count);
        Assert.Equal("Abcd Efgh", headings[0].Text);
        Assert.Equal("IJK", headings[1].Text);
        Assert.Equal("lmn", headings[2].Text);
    }

    [Fact]
    public void Parse_CodeBlockText_ExcludesFences()
    {
        const string input = "```\nabcd\nefgh\n```\n";
        var root = MarkdownTreeParser.Parse(input);
        var codeBlocks = CollectNodes(root, MarkdownNode.Type.CodeBlock);

        Assert.Single(codeBlocks);
        Assert.Equal("abcd\nefgh", codeBlocks[0].Text);
    }

    [Fact]
    public void Parse_TildeCodeBlock_ExcludesFences()
    {
        const string input = "~~~\nabc\ndef\n~~~\n";
        var root = MarkdownTreeParser.Parse(input);
        var codeBlocks = CollectNodes(root, MarkdownNode.Type.CodeBlock);

        Assert.Single(codeBlocks);
        Assert.Equal("abc\ndef", codeBlocks[0].Text);
    }

    [Fact]
    public void Parse_HeadingInsideCodeBlock_IsNotParsedAsHeading()
    {
        const string input = "```\n# Not Heading\n```\n# Heading\n";
        var root = MarkdownTreeParser.Parse(input);
        var headings = CollectNodes(root, MarkdownNode.Type.H1);

        Assert.Single(headings);
        Assert.Equal("Heading", headings[0].Text);
    }

    [Fact]
    public void Parse_UnorderedList_CreatesCorrectStructure()
    {
        const string input = "- alpha\n- bravo\n- charlie\n";
        var root = MarkdownTreeParser.Parse(input);
        var lists = CollectNodes(root, MarkdownNode.Type.UnorderedList);

        Assert.Single(lists);
        var list = lists[0];
        Assert.Equal(3, list.Children.Count);
        Assert.All(list.Children, c => Assert.Equal(MarkdownNode.Type.ListItem, c.NodeType));
        Assert.Equal("alpha", list.Children[0].Text);
        Assert.Equal("bravo", list.Children[1].Text);
        Assert.Equal("charlie", list.Children[2].Text);
    }

    [Fact]
    public void Parse_OrderedList_CreatesCorrectStructure()
    {
        const string input = "1. first\n2. second\n3. third\n";
        var root = MarkdownTreeParser.Parse(input);
        var lists = CollectNodes(root, MarkdownNode.Type.OrderedList);

        Assert.Single(lists);
        var list = lists[0];
        Assert.Equal(3, list.Children.Count);
        Assert.All(list.Children, c => Assert.Equal(MarkdownNode.Type.ListItem, c.NodeType));
        Assert.Equal("first", list.Children[0].Text);
        Assert.Equal("second", list.Children[1].Text);
        Assert.Equal("third", list.Children[2].Text);
    }

    [Fact]
    public void Parse_ListItemWithContinuation_IncludesContinuationText()
    {
        const string input = "- item one\n  continuation\n- item two\n";
        var root = MarkdownTreeParser.Parse(input);
        var items = CollectNodes(root, MarkdownNode.Type.ListItem);

        Assert.Equal(2, items.Count);
        Assert.Equal("item one\ncontinuation", items[0].Text);
        Assert.Equal("item two", items[1].Text);
    }

    [Fact]
    public void Parse_HeadingNesting_ChildrenAreUnderCorrectParent()
    {
        const string input = "# Title\n## Sub A\n## Sub B\n# Title 2\n";
        var root = MarkdownTreeParser.Parse(input);

        var h1Nodes = CollectNodes(root, MarkdownNode.Type.H1);
        Assert.Equal(2, h1Nodes.Count);

        // First H1 should contain both H2s as children:
        Assert.Equal(2, h1Nodes[0].Children.Count);
        Assert.Equal(MarkdownNode.Type.H2, h1Nodes[0].Children[0].NodeType);
        Assert.Equal(MarkdownNode.Type.H2, h1Nodes[0].Children[1].NodeType);

        // Second H1 should have no children:
        Assert.Empty(h1Nodes[1].Children);
    }

    [Fact]
    public void Parse_DeepHeadingNesting_BuildsCorrectTree()
    {
        const string input = "# H1\n## H2\n### H3\n#### H4\n##### H5\n###### H6\n";
        var root = MarkdownTreeParser.Parse(input);

        var h1 = Assert.Single(CollectNodes(root, MarkdownNode.Type.H1));
        var h2 = Assert.Single(h1.Children);
        Assert.Equal(MarkdownNode.Type.H2, h2.NodeType);
        var h3 = Assert.Single(h2.Children);
        Assert.Equal(MarkdownNode.Type.H3, h3.NodeType);
        var h4 = Assert.Single(h3.Children);
        Assert.Equal(MarkdownNode.Type.H4, h4.NodeType);
        var h5 = Assert.Single(h4.Children);
        Assert.Equal(MarkdownNode.Type.H5, h5.NodeType);
        var h6 = Assert.Single(h5.Children);
        Assert.Equal(MarkdownNode.Type.H6, h6.NodeType);
    }

    [Fact]
    public void Parse_ThematicBreaks_AreDetected()
    {
        const string input = "---\n***\n___\n";
        var root = MarkdownTreeParser.Parse(input);
        var hrs = CollectNodes(root, MarkdownNode.Type.ThematicBreak);

        Assert.Equal(3, hrs.Count);
    }

    [Fact]
    public void Parse_HeadingEndOffset_SpansEntireSection()
    {
        const string input = "# Title\nSome paragraph\n\n## Sub\nMore text\n";
        var root = MarkdownTreeParser.Parse(input);
        var h1 = Assert.Single(CollectNodes(root, MarkdownNode.Type.H1));

        // H1 end offset should extend to cover its children (paragraph + H2 section):
        Assert.True(h1.EndOffset > h1.StartOffset + "# Title\n".Length);
        Assert.Equal(input.Length, h1.EndOffset);
    }

    [Fact]
    public void Parse_ParagraphBreaksOnHeading()
    {
        const string input = "line one\nline two\n# Heading\nline three\n";
        var root = MarkdownTreeParser.Parse(input);
        var paragraphs = CollectNodes(root, MarkdownNode.Type.Paragraph);

        Assert.Equal(2, paragraphs.Count);
        Assert.Equal("line one\nline two", paragraphs[0].Text);
        Assert.Equal("line three", paragraphs[1].Text);
    }

    [Fact]
    public void Parse_ParagraphBreaksOnList()
    {
        const string input = "some text\n- item one\n- item two\n";
        var root = MarkdownTreeParser.Parse(input);
        var paragraphs = CollectNodes(root, MarkdownNode.Type.Paragraph);
        var lists = CollectNodes(root, MarkdownNode.Type.UnorderedList);

        Assert.Single(paragraphs);
        Assert.Single(lists);
        Assert.Equal("some text", paragraphs[0].Text);
    }

    [Fact]
    public void Parse_AllNodesReachable_FromRoot()
    {
        var random = new Random(Seed);
        var input = GenerateDocument(random, 80);
        var root = MarkdownTreeParser.Parse(input);

        // Count nodes via traversal:
        var count = 0;
        Traverse(root);

        // Count nodes via references:
        var allNodes = new HashSet<MarkdownNode>();
        Collect(root);

        Assert.Equal(count, allNodes.Count);
        Assert.True(count > 10, $"Expected more than 10 nodes, got {count}");
        return;

        void Collect(MarkdownNode n)
        {
            Assert.True(allNodes.Add(n), "Duplicate node encountered in tree walk");
            
            foreach (var c in n.Children)
            {
                Collect(c);
            }
        }

        void Traverse(MarkdownNode n)
        {
            count++;
            
            foreach (var c in n.Children)
            {
                Traverse(c);
            }
        }
    }

    [Fact]
    public void Parse_MixedListTypes_CreateSeparateListNodes()
    {
        const string input = "- ul item 1\n- ul item 2\n\n1. ol item 1\n2. ol item 2\n";
        var root = MarkdownTreeParser.Parse(input);
        var unorderedListNodes = CollectNodes(root, MarkdownNode.Type.UnorderedList);
        var orderedListNodes = CollectNodes(root, MarkdownNode.Type.OrderedList);

        Assert.Single(unorderedListNodes);
        Assert.Single(orderedListNodes);
        Assert.Equal(2, unorderedListNodes[0].Children.Count);
        Assert.Equal(2, orderedListNodes[0].Children.Count);
    }

    [Fact]
    public void Parse_CodeBlockLanguageIdentifier_IsExtracted()
    {
        const string input = "```cs\nvar x = 1;\n```\n";
        var root = MarkdownTreeParser.Parse(input);
        var codeBlocks = CollectNodes(root, MarkdownNode.Type.CodeBlock);

        Assert.Single(codeBlocks);
        Assert.Equal("cs", codeBlocks[0].Language);
        Assert.Equal("var x = 1;", codeBlocks[0].Text);
    }

    [Fact]
    public void Parse_CodeBlockNoLanguage_EmptyLanguage()
    {
        const string input = "```\nvar x = 1;\n```\n";
        var root = MarkdownTreeParser.Parse(input);
        var codeBlocks = CollectNodes(root, MarkdownNode.Type.CodeBlock);

        Assert.Single(codeBlocks);
        Assert.Equal(string.Empty, codeBlocks[0].Language);
    }

    [Fact]
    public void Parse_CodeBlockTildeWithLanguage_IsExtracted()
    {
        const string input = "~~~kt\nval y = 2\n~~~\n";
        var root = MarkdownTreeParser.Parse(input);
        var codeBlocks = CollectNodes(root, MarkdownNode.Type.CodeBlock);

        Assert.Single(codeBlocks);
        Assert.Equal("kt", codeBlocks[0].Language);
        Assert.Equal("val y = 2", codeBlocks[0].Text);
    }

    [Fact]
    public void Parse_TabIndentedHeading_IsParsed()
    {
        const string input = "\t# Heading\n";
        var root = MarkdownTreeParser.Parse(input);
        var headings = CollectNodes(root, MarkdownNode.Type.H1);

        Assert.Single(headings);
        Assert.Equal("Heading", headings[0].Text);
    }

    [Fact]
    public void Parse_TabIndentedCodeBlock_IsParsed()
    {
        const string input = "\t```cs\ncode\n\t```\n";
        var root = MarkdownTreeParser.Parse(input);
        var codeBlocks = CollectNodes(root, MarkdownNode.Type.CodeBlock);

        Assert.Single(codeBlocks);
        Assert.Equal("cs", codeBlocks[0].Language);
    }

    [Fact]
    public void Parse_TabIndentedUnorderedList_IsParsed()
    {
        const string input = "\t- item one\n";
        var root = MarkdownTreeParser.Parse(input);
        var items = CollectNodes(root, MarkdownNode.Type.ListItem);

        Assert.Single(items);
        Assert.Equal("item one", items[0].Text);
    }

    [Fact]
    public void Parse_TabIndentedOrderedList_IsParsed()
    {
        const string input = "\t1. first\n";
        var root = MarkdownTreeParser.Parse(input);
        var items = CollectNodes(root, MarkdownNode.Type.ListItem);

        Assert.Single(items);
        Assert.Equal("first", items[0].Text);
    }

    [Fact]
    public void Parse_TabIndentedThematicBreak_IsParsed()
    {
        const string input = "\t---\n";
        var root = MarkdownTreeParser.Parse(input);
        var hrs = CollectNodes(root, MarkdownNode.Type.ThematicBreak);

        Assert.Single(hrs);
    }

    [Fact]
    public void Parse_TabContinuationInListItem_IncludesContinuation()
    {
        const string input = "- item one\n\tcontinuation\n";
        var root = MarkdownTreeParser.Parse(input);
        var items = CollectNodes(root, MarkdownNode.Type.ListItem);

        Assert.Single(items);
        Assert.Equal("item one\ncontinuation", items[0].Text);
    }

    [Fact]
    public void Parse_Blockquote_SingleLine()
    {
        const string input = "> quoted text\n";
        var root = MarkdownTreeParser.Parse(input);
        var blockquotes = CollectNodes(root, MarkdownNode.Type.Blockquote);

        Assert.Single(blockquotes);
        Assert.Equal("quoted text", blockquotes[0].Text);
    }

    [Fact]
    public void Parse_Blockquote_MultiLine()
    {
        const string input = "> line one\n> line two\n";
        var root = MarkdownTreeParser.Parse(input);
        var blockquotes = CollectNodes(root, MarkdownNode.Type.Blockquote);

        Assert.Single(blockquotes);
        Assert.Equal("line one\nline two", blockquotes[0].Text);
    }

    [Fact]
    public void Parse_Blockquote_BreaksParagraph()
    {
        const string input = "paragraph text\n> blockquote\n";
        var root = MarkdownTreeParser.Parse(input);
        var paragraphs = CollectNodes(root, MarkdownNode.Type.Paragraph);
        var blockquotes = CollectNodes(root, MarkdownNode.Type.Blockquote);

        Assert.Single(paragraphs);
        Assert.Single(blockquotes);
        Assert.Equal("paragraph text", paragraphs[0].Text);
        Assert.Equal("blockquote", blockquotes[0].Text);
    }

    [Fact]
    public void Parse_Blockquote_IndentedWithTab()
    {
        const string input = "\t> quoted\n";
        var root = MarkdownTreeParser.Parse(input);
        var blockquotes = CollectNodes(root, MarkdownNode.Type.Blockquote);

        Assert.Single(blockquotes);
        Assert.Equal("quoted", blockquotes[0].Text);
    }
}