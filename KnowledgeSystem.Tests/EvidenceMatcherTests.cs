using KnowledgeSystem.Retrieval.Graph.Extraction;

namespace KnowledgeSystem.Tests;

public class EvidenceMatcherTests
{
    #region Exact match

    [Fact]
    public void TryMatch_ExactMatch_Succeeds()
    {
        const string source = "Jim Holden saw some poetry in that.";
        var result = EvidenceMatcher.TryMatch(source, "Jim Holden saw some poetry");

        Assert.True(result.IsSuccess);
        Assert.Null(result.FailureDiagnostic);
        Assert.Equal("Jim Holden saw some poetry", result.Evidence!.QuotedText);
        Assert.Equal(0, result.Evidence.Span!.Value.Start);
    }

    [Fact]
    public void TryMatch_ExactMatch_MidString_Succeeds()
    {
        const string source = "Then Solomon Epstein had built his little modified fusion drive";
        var result = EvidenceMatcher.TryMatch(source, "Solomon Epstein had built");

        Assert.True(result.IsSuccess);
        Assert.Equal("Solomon Epstein had built", result.Evidence!.QuotedText);
    }

    [Fact]
    public void TryMatch_NoMatch_ReturnsDiagnostic()
    {
        const string source = "The Canterbury was a retooled colony transport.";
        var result = EvidenceMatcher.TryMatch(source, "This text does not appear anywhere in the source");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.FailureDiagnostic);
    }

    #endregion

    #region Markdown Stripping

    [Fact]
    public void TryMatch_MarkdownItalics_StrippedAndMatched()
    {
        const string source = "*Knight*'s landing gear isn't going to be good in atmosphere";
        var result = EvidenceMatcher.TryMatch(source, "Knight's landing gear isn't going to be good");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Evidence);
    }

    [Fact]
    public void TryMatch_MarkdownBold_StrippedAndMatched()
    {
        const string source = "The **Canterbury** was a retooled colony transport.";
        var result = EvidenceMatcher.TryMatch(source, "The Canterbury was a retooled colony transport.");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TryMatch_MarkdownStrikethrough_StrippedAndMatched()
    {
        const string source = "The ~~old ship~~ Canterbury sailed on.";
        var result = EvidenceMatcher.TryMatch(source, "The old ship Canterbury sailed on.");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TryMatch_MarkdownHeading_StrippedAndMatched()
    {
        const string source = "# Chapter One\nThe beginning.";
        var result = EvidenceMatcher.TryMatch(source, "Chapter One");

        Assert.True(result.IsSuccess);
    }

    #endregion

    #region Unicode punctuation normalization

    [Fact]
    public void TryMatch_SmartDoubleQuotes_NormalizedToStraight()
    {
        const string source = "\u201CYou\u2019re not going anyplace,\u201D she said.";
        var result = EvidenceMatcher.TryMatch(source, "You're not going anyplace");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TryMatch_SmartSingleQuotes_NormalizedToStraight()
    {
        const string source = "She said \u2018hello\u2019 and walked away.";
        var result = EvidenceMatcher.TryMatch(source, "She said 'hello' and walked away.");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TryMatch_Ellipsis_NormalizedToThreeDots()
    {
        const string source = "Then Solomon Epstein had built his little\u2026";
        var result = EvidenceMatcher.TryMatch(source, "Then Solomon Epstein had built his little...");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TryMatch_Emdash_NormalizedToHyphen()
    {
        const string source = "The ship\u2014a century old\u2014sailed on.";
        var result = EvidenceMatcher.TryMatch(source, "The ship-a century old-sailed on.");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TryMatch_Endash_NormalizedToHyphen()
    {
        const string source = "pages 10\u201320 were missing.";
        var result = EvidenceMatcher.TryMatch(source, "pages 10-20 were missing.");

        Assert.True(result.IsSuccess);
    }

    #endregion

    #region Case-insensitive fallback

    [Fact]
    public void TryMatch_CaseInsensitive_NormalizedFallback()
    {
        const string source = "Chief Engineer Naomi Nagata towered over him.";
        var result = EvidenceMatcher.TryMatch(source, "chief engineer naomi nagata towered over him.");

        Assert.True(result.IsSuccess);
    }

    #endregion

    #region Whitespace normalization

    [Fact]
    public void TryMatch_CollapsedWhitespace_Matches()
    {
        const string source = "The   Canterbury    was  a  retooled colony transport.";
        var result = EvidenceMatcher.TryMatch(source, "The Canterbury was a retooled colony transport.");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TryMatch_NewlineCollapsed_Matches()
    {
        const string source = "The Canterbury\nwas a retooled\ncolony transport.";
        var result = EvidenceMatcher.TryMatch(source, "The Canterbury was a retooled colony transport.");

        Assert.True(result.IsSuccess);
    }

    #endregion

    #region Prefix fallback (LLM drift at end)

    [Fact]
    public void TryMatch_PrefixFallback_WhenEndDrifts()
    {
        const string source = "Chief Engineer Naomi Nagata towered over him.";
        var result = EvidenceMatcher.TryMatch(source, "Chief Engineer Naomi Nagata towered over him with great height");

        Assert.True(result.IsSuccess);
        Assert.Contains("Chief Engineer Naomi Nagata", result.Evidence!.QuotedText);
    }

    [Fact]
    public void TryMatch_ShortPrefix_StillMatches()
    {
        const string source = "Jim Holden saw some poetry in that.";
        var result = EvidenceMatcher.TryMatch(source, "Jim Holden saw some poetry in that completely different ending text");

        Assert.True(result.IsSuccess);
    }

    #endregion

    #region Non-contiguous span (no match)

    [Fact]
    public void TryMatch_NonContiguousSpan_ReturnsDiagnostic()
    {
        const string source = "the Canterbury and her dozens of sister ships in the Pur'n'Kleen Water Company made the loop";
        var result = EvidenceMatcher.TryMatch(source, "the Pur'n'Kleen Water Company made the loop");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TryMatch_CompletelyUnrelated_ReturnsNoMatchDiagnostic()
    {
        const string source = "The Canterbury was a retooled colony transport.";
        var result = EvidenceMatcher.TryMatch(source, "Zaphod Beeblebrox stole the Heart of Gold");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.FailureDiagnostic);
        Assert.Contains("No matching text found", result.FailureDiagnostic!);
    }

    #endregion

    #region Real-world failure scenarios from the extraction log with MiMo 2.5

    [Fact]
    public void TryMatch_KnightEntity_MarkdownStripping()
    {
        const string source = "*Knight*'s landing gear isn't going to be good in atmosphere until I can get the seals replaced.";
        var result = EvidenceMatcher.TryMatch(source, "Knight's landing gear isn't going to be good in");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TryMatch_PurnKleenEntity_NonContiguousEvidence()
    {
        const string source = "the *Canterbury* and her dozens of sister ships in the Pur'n'Kleen Water Company made the loop from Saturn's generous rings to the Belt";
        var result = EvidenceMatcher.TryMatch(source, "the Pur'n'Kleen Water Company made the loop fro");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TryMatch_ScopuliEntity_CaseInsensitivePrefix()
    {
        const string source = "Light freighter. Martian registry. Shows Eros as home port. Calls itself *Scopuli*.";
        var result = EvidenceMatcher.TryMatch(source, "light freighter. Martian registry. Shows Eros");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TryMatch_Dialogue_SmartQuotes()
    {
        const string source = "\u201CI\u2019m not up for sex tonight.\u201D";
        var result = EvidenceMatcher.TryMatch(source, "I'm not up for sex tonight.");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TryMatch_Dialogue_YoureComfortableHere()
    {
        const string source = "\u201CYou\u2019re comfortable here.\u201D Her eyes were less kind now.";
        var result = EvidenceMatcher.TryMatch(source, "You're comfortable here.");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TryMatch_CenturyOldIceHauler_Dialogue()
    {
        const string source = "\u201CThe Cant\u2019s a century-old ice hauler.\u201D";
        var result = EvidenceMatcher.TryMatch(source, "The Cant's a century-old ice hauler.");

        Assert.True(result.IsSuccess);
    }

    #endregion

    #region Diagnostic Quality

    [Fact]
    public void TryMatch_PartialMatch_ReturnsClosestSourceContext()
    {
        const string source = "The Canterbury was a retooled colony transport. Once it had been packed with people.";
        var result = EvidenceMatcher.TryMatch(source, "The Canterbury was a retooled colony transport with extra wrong words at the end that don't exist");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TryMatch_NoMatchAtAll_ReturnsGenericDiagnostic()
    {
        const string source = "The quick brown fox jumps over the lazy dog.";
        var result = EvidenceMatcher.TryMatch(source, "XYZABC completely unrelated text here");

        Assert.False(result.IsSuccess);
        Assert.Equal("No matching text found. Use an exact contiguous substring from the source.", result.FailureDiagnostic);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void TryMatch_EmptyEvidence_SucceedsAtStart()
    {
        const string source = "Some source text.";
        var result = EvidenceMatcher.TryMatch(source, "");

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Evidence!.Span!.Value.Start);
        Assert.Equal(0, result.Evidence.Span.Value.Length);
    }

    [Fact]
    public void TryMatch_VeryShortEvidence_ReturnsDiagnostic()
    {
        const string source = "Some source text.";
        var result = EvidenceMatcher.TryMatch(source, "Sox");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void TryMatch_EvidenceEqualsSource_Succeeds()
    {
        var source = "Jim Holden saw some poetry in that.";
        var result = EvidenceMatcher.TryMatch(source, source);

        Assert.True(result.IsSuccess);
        Assert.Equal(source, result.Evidence!.QuotedText);
    }

    [Fact]
    public void TryMatch_EllipsisInSource_MappedCorrectly()
    {
        const string source = "He said\u2026 and then left.";
        var result = EvidenceMatcher.TryMatch(source, "He said... and then left.");

        Assert.True(result.IsSuccess);
        Assert.StartsWith("He said", result.Evidence!.QuotedText);
        Assert.Contains("\u2026", result.Evidence.QuotedText);
    }

    #endregion
}
