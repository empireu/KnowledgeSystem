using System.Text;
using KnowledgeSystem.Retrieval.Api.Graph;
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Retrieval.Graph.Extraction;

/// <summary>
///     Matches evidence strings against source text, with fallback to normalized matching that strips Markdown formatting, normalizes Unicode punctuation, collapses whitespace, and tries case-insensitive matching.
/// </summary>
internal static class EvidenceMatcher
{
    /// <summary>
    ///     Characters stripped during normalization (markdown formatting).
    /// </summary>
    private static readonly char[] MarkdownChars = ['*', '_', '~', '#'];

    /// <summary>
    ///     Result of an evidence match attempt, carrying either a successful match or a diagnostic message for the LLM.
    /// </summary>
    public readonly record struct EvidenceMatchResult(RawEvidence? Evidence, string? FailureDiagnostic)
    {
        public bool IsSuccess => Evidence != null;
    }

    /// <summary>
    ///     Attempts to find the evidence text in the source content.
    ///     First tries exact match, then falls back to normalized match (markdown/whitespace/Unicode normalized), then case-insensitive normalized match, then progressively shorter prefixes.
    /// </summary>
    /// <param name="sourceContent">The full source text.</param>
    /// <param name="evidenceText">The evidence string to locate.</param>
    /// <returns>An <see cref="EvidenceMatchResult"/> with either a successful match or a diagnostic hint.</returns>
    public static EvidenceMatchResult TryMatch(string sourceContent, string evidenceText)
    {
        var exactIndex = sourceContent.IndexOf(evidenceText, StringComparison.Ordinal);
        if (exactIndex >= 0)
        {
            var span = new TextSpan(exactIndex, evidenceText.Length);
            return new EvidenceMatchResult(new RawEvidence(evidenceText, sourceContent, span), null);
        }

        // Normalized match:
        var normalizedSource = Normalize(sourceContent);
        var normalizedEvidence = Normalize(evidenceText);

        // Try full normalized match, then progressively shorter prefixes.
        // This handles cases where the model gets the start right but drifts at the end.
        const int minimumPrefixLength = 6;
        var maxPrefixLength = normalizedEvidence.Length;

        for (var prefixLength = maxPrefixLength; prefixLength >= minimumPrefixLength; prefixLength -= 3)
        {
            var prefix = normalizedEvidence[..prefixLength];
            
            // Ensure we break at a word boundary:
            var lastSpace = prefix.LastIndexOf(' ');
            if (lastSpace > minimumPrefixLength)
            {
                prefix = prefix[..lastSpace];
            }

            var normalizedIndex = normalizedSource.IndexOf(prefix, StringComparison.Ordinal);
            if (normalizedIndex < 0)
            {
                normalizedIndex = normalizedSource.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            }

            if (normalizedIndex < 0)
            {
                continue;
            }

            var originalOffset = MapNormalizedIndexToOriginal(sourceContent, normalizedIndex);
            if (originalOffset < 0)
            {
                continue;
            }

            var normalizedEnd = normalizedIndex + prefix.Length;
            var originalEnd = MapNormalizedIndexToOriginal(sourceContent, normalizedEnd);
            if (originalEnd < 0)
            {
                originalEnd = sourceContent.Length;
            }

            var spanLength = Math.Max(originalEnd - originalOffset, 1);
            var actualQuotedText = sourceContent.Substring(originalOffset, Math.Min(spanLength, sourceContent.Length - originalOffset));

            return new EvidenceMatchResult(new RawEvidence(actualQuotedText, sourceContent, new TextSpan(originalOffset, actualQuotedText.Length)), null);
        }

        // Find the best anchor point in the source to guide the model:
        var diagnostic = BuildDiagnostic(sourceContent, normalizedSource, normalizedEvidence);

        return new EvidenceMatchResult(null, diagnostic);
    }

    /// <summary>
    ///     Builds a diagnostic message by finding the longest prefix of the normalized evidence that appears in the normalized source, then extracting surrounding context.
    /// </summary>
    private static string BuildDiagnostic(string sourceContent, string normalizedSource, string normalizedEvidence)
    {
        // Try progressively shorter anchors until we find one in the source:
        for (var anchorLength = Math.Min(25, normalizedEvidence.Length); anchorLength >= 6; anchorLength -= 4)
        {
            var anchor = normalizedEvidence[..anchorLength];
            var lastSpace = anchor.LastIndexOf(' ');
            if (lastSpace > 4)
            {
                anchor = anchor[..lastSpace];
            }

            var anchorIndex = normalizedSource.IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
            if (anchorIndex < 0)
            {
                continue;
            }

            // Found an anchor. Extract surrounding source context:
            var contextStart = MapNormalizedIndexToOriginal(sourceContent, anchorIndex);
            var contextEndNorm = Math.Min(anchorIndex + 80, normalizedSource.Length);
            var contextEnd = MapNormalizedIndexToOriginal(sourceContent, contextEndNorm);
            if (contextStart < 0 || contextEnd <= contextStart)
            {
                continue;
            }

            var contextLength = Math.Min(contextEnd - contextStart, 100);
            var contextText = sourceContent.Substring(contextStart, contextLength);
            var truncated = contextText.Length > 60 ? contextText[..57] + "..." : contextText;

            return $"Closest source text: \"{truncated}\". Use exact contiguous wording.";
        }

        return "No matching text found. Use an exact contiguous substring from the source.";
    }

    /// <summary>
    ///     Normalizes a string for fuzzy matching: strips Markdown formatting characters, normalizes Unicode punctuation to ASCII equivalents, and collapses consecutive whitespace to single spaces.
    /// </summary>
    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var previousWasSpace = false;

        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];

            // Strip Markdown formatting characters:
            if (Array.IndexOf(MarkdownChars, c) >= 0)
            {
                continue;
            }

            // Normalize Unicode punctuation to ASCII equivalents:
            if (c is '\u201C' or '\u201D')
            {
                sb.Append('"');
                previousWasSpace = false;
                continue;
            }

            if (c is '\u2018' or '\u2019')
            {
                sb.Append('\'');
                previousWasSpace = false;
                continue;
            }

            if (c is '\u2014' or '\u2013')
            {
                sb.Append('-');
                previousWasSpace = false;
                continue;
            }

            if (c == '\u2026')
            {
                sb.Append("...");
                previousWasSpace = false;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!previousWasSpace)
                {
                    sb.Append(' ');
                    previousWasSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                previousWasSpace = false;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Maps a character index in the normalized version of <paramref name="source"/> back to the corresponding index in the original source string.
    /// </summary>
    private static int MapNormalizedIndexToOriginal(string source, int normalizedIndex)
    {
        var normalizedPosition = 0;
        var previousWasSpace = false;

        for (var i = 0; i < source.Length; i++)
        {
            if (normalizedPosition >= normalizedIndex)
            {
                return i;
            }

            var c = source[i];

            // Ellipsis expands to 3 characters in normalized form:
            if (c == '\u2026')
            {
                if (normalizedPosition + 3 > normalizedIndex)
                {
                    return i;
                }
                normalizedPosition += 3;
                previousWasSpace = false;
                continue;
            }

            // 1:1 Unicode normalizations (quotes, dashes) produce one normalized character:
            if (c is '\u201C' or '\u201D' or '\u2018' or '\u2019' or '\u2014' or '\u2013')
            {
                normalizedPosition++;
                previousWasSpace = false;
                continue;
            }

            if (Array.IndexOf(MarkdownChars, c) >= 0)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!previousWasSpace)
                {
                    normalizedPosition++;
                    previousWasSpace = true;
                }
            }
            else
            {
                normalizedPosition++;
                previousWasSpace = false;
            }
        }

        return normalizedPosition >= normalizedIndex ? source.Length : -1;
    }
}
