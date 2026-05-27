using System.Text;
using FastContextToolHandler = KnowledgeSystem.Plugins.Wiki.Tools.FastContext.FastContextToolHandler;

namespace KnowledgeSystem.Tests;

public class FastContextTests
{
    #region AppendSnippets

    [Fact]
    public void AppendSnippets_NoMatch_ReturnsNothing()
    {
        var sb = new StringBuilder();
        var tokens = new[] { "nonexistent" };

        FastContextToolHandler.AppendSnippets(sb, "some text here", 100, tokens, 35, 10, 4);

        Assert.Equal(string.Empty, sb.ToString());
    }

    [Fact]
    public void AppendSnippets_SingleMatch_PrintsSnippet()
    {
        var sb = new StringBuilder();
        var tokens = new[] { "target" };

        FastContextToolHandler.AppendSnippets(sb, "prefix target suffix", 50, tokens, 35, 10, 4);
        var result = sb.ToString();

        Assert.Contains("target", result);
        Assert.Contains("'prefix target suffix':", result);
    }

    [Fact]
    public void AppendSnippets_DocumentOffsetsCorrect()
    {
        var sb = new StringBuilder();
        var tokens = new[] { "match" };
        const int nodeStartOffset = 100;

        FastContextToolHandler.AppendSnippets(sb, "some match here", nodeStartOffset, tokens, 35, 10, 4);
        var result = sb.ToString();
        
        Assert.Contains(":100,115", result);
    }

    [Fact]
    public void AppendSnippets_CaseInsensitive()
    {
        var sb = new StringBuilder();
        var tokens = new[] { "TARGET" };

        FastContextToolHandler.AppendSnippets(sb, "prefix target suffix", 0, tokens, 35, 10, 4);
        var result = sb.ToString();

        Assert.Contains("target", result);
    }

    [Fact]
    public void AppendSnippets_MultipleTokens_GetsCoverage()
    {
        var sb = new StringBuilder();
        var tokens = new[] { "alpha", "bravo" };
        var text = "first alpha second\nthird bravo fourth";

        FastContextToolHandler.AppendSnippets(sb, text, 0, tokens, 35, 10, 4);
        var result = sb.ToString();

        Assert.Contains("alpha", result);
        Assert.Contains("bravo", result);
    }

    [Fact]
    public void AppendSnippets_SameTokenTwice_MergesNearbyWindows()
    {
        var sb = new StringBuilder();
        var tokens = new[] { "hit" };
        const string text = "hit one two three four hit";

        FastContextToolHandler.AppendSnippets(sb, text, 0, tokens, 35, 10, 4);
        var result = sb.ToString();

        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
    }

    [Fact]
    public void AppendSnippets_RespectsLineBoundaries()
    {
        var sb = new StringBuilder();
        var tokens = new[] { "target" };
        const string text = "prefix\ntarget\nsuffix";

        FastContextToolHandler.AppendSnippets(sb, text, 0, tokens, 35, 10, 4);
        var result = sb.ToString();

        Assert.Contains("target", result);
        Assert.DoesNotContain("prefix", result);
        Assert.DoesNotContain("suffix", result);
    }

    [Fact]
    public void AppendSnippets_SnappedToWordBoundaries()
    {
        var sb = new StringBuilder();
        var tokens = new[] { "lit" };
        const string text = "little majesty";

        FastContextToolHandler.AppendSnippets(sb, text, 0, tokens, 35, 10, 4);
        var result = sb.ToString();

        Assert.Contains("little", result);
    }

    [Fact]
    public void AppendSnippets_EmptyNodeText_ReturnsNothing()
    {
        var sb = new StringBuilder();
        var tokens = new[] { "anything" };

        FastContextToolHandler.AppendSnippets(sb, "", 0, tokens, 35, 10, 4);

        Assert.Equal(string.Empty, sb.ToString());
    }

    [Fact]
    public void AppendSnippets_EmptyTokens_ReturnsNothing()
    {
        var sb = new StringBuilder();

        FastContextToolHandler.AppendSnippets(sb, "some text", 0, Array.Empty<string>(), 35, 10, 4);

        Assert.Equal(string.Empty, sb.ToString());
    }

    [Fact]
    public void AppendSnippets_SnippetIncludesSurroundingContext()
    {
        var sb = new StringBuilder();
        var tokens = new[] { "middle" };
        const string text = "alpha beta gamma middle delta epsilon zeta";

        FastContextToolHandler.AppendSnippets(sb, text, 10, tokens, 35, 10, 4);
        var result = sb.ToString();

        Assert.Contains("middle", result);
        Assert.Contains("gamma", result);
        Assert.Contains("delta", result);
    }

    #endregion
}