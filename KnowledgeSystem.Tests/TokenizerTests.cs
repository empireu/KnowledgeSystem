using KnowledgeSystem.Lexical;

namespace KnowledgeSystem.Tests;

public class TokenizerTests
{
    [Fact]
    public void TokenizeQuery_IncludesFullQuery()
    {
        var tokens = Tokenizer.TokenizeQuery("DX1 system config", true);

        Assert.Contains("DX1 system config", tokens);
    }

    [Fact]
    public void TokenizeQuery_FiltersBlacklistedWords()
    {
        var tokens = Tokenizer.TokenizeQuery("the and for are but", true);

        Assert.DoesNotContain("the", tokens);
        Assert.DoesNotContain("and", tokens);
        Assert.DoesNotContain("for", tokens);
    }

    [Fact]
    public void TokenizeQuery_FiltersShortWords()
    {
        var tokens = Tokenizer.TokenizeQuery("a bc de fgh", true);

        Assert.DoesNotContain("a", tokens);
        Assert.Contains("bc", tokens);
        Assert.Contains("de", tokens);
        Assert.Contains("fgh", tokens);
    }

    [Fact]
    public void TokenizeQuery_IsCaseInsensitive()
    {
        var tokens = Tokenizer.TokenizeQuery("DX1 dx1 Dx1", true);

        Assert.Single(tokens, t => t.Equals("DX1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TokenizeQuery_HandlesPunctuation()
    {
        var tokens = Tokenizer.TokenizeQuery("DX1, system-config (alpha)!", true);

        Assert.Contains("DX1", tokens);
        Assert.Contains("system", tokens);
        Assert.Contains("config", tokens);
        Assert.Contains("alpha", tokens);
    }

    [Fact]
    public void TokenizeQuery_EmptyQuery_ReturnsEmpty()
    {
        var tokens = Tokenizer.TokenizeQuery("", true);

        Assert.Empty(tokens);
    }

    [Fact]
    public void TokenizeQuery_ShortQuery_UnderThreeChars_ReturnsEmpty()
    {
        var tokens = Tokenizer.TokenizeQuery("", true);

        Assert.Empty(tokens);
    }

    [Fact]
    public void TokenizeQuery_PreservesMeaningfulWords()
    {
        var tokens = Tokenizer.TokenizeQuery("Quick Response Force deployment", true);

        Assert.Contains("Quick", tokens);
        Assert.Contains("Response", tokens);
        Assert.Contains("Force", tokens);
        Assert.Contains("deployment", tokens);
    }
}