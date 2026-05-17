using System.Text;
using KnowledgeSystem.EmdParser.MarkdownTree;
using KnowledgeSystem.Retrieval;

namespace KnowledgeSystem.Agent.Tools;

public sealed partial class FastContextToolHandler
{
    /// <summary>
    ///     Pulls and formats references so the content can be inspected with other tools.
    /// </summary>
    private void CompactExtraction(StringBuilder sb, string query, List<FastContextRetrieval.ReferencedDocument> results)
    {
        sb.AppendLine("fast_context: Too much content found. Here are the paths, offsets `a,b`, sections written as `@XXX` (if they exist), and snippets with their offsets `'…and the query is…':x,y` of the most relevant results for targeted inspection:");

        var tokens = TokenizeQuery(query, config.TokenizerBlacklist);

        string? currentDir = null;
        foreach (var referencedDocument in results)
        {
            var path = referencedDocument.Document.Path;
            var lastSlash = path.LastIndexOf('/');
            var dir = lastSlash >= 0 ? path[..lastSlash] : "";
            var fileName = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;

            if (dir != currentDir)
            {
                currentDir = dir;
                sb.AppendLine();
                sb.AppendLine($"{dir}/");
            }

            sb.AppendLine($"  {fileName}");

            var content = referencedDocument.Document.Content;

            foreach (var boundingTree in referencedDocument.BoundingTreesSorted.OrderBy(t => t.Root.StartOffset))
            {
                var root = boundingTree.Root;
                var start = root.StartOffset;
                var end = root.EndOffset;

                if (root.NodeType.IsHeading())
                {
                    sb.AppendLine($"    {start},{end} (@{root.Text})");
                }
                else
                {
                    sb.AppendLine($"    {start},{end}");
                }

                var nodeText = content[start..end];

                AppendSnippets(sb, nodeText, start, tokens, config.SnippetContext, config.MaxWindows, config.DesiredSnippets);
            }
        }
    }

    /// <summary>
    ///     Tokenizes the query into specific words (excludes the <see cref="Tools.FastContextToolHandler.DefaultBlacklistedWords"/>). The result includes the query itself.
    /// </summary>
    internal static string[] TokenizeQuery(string query, HashSet<string>? blacklist = null)
    {
        query = query.Trim();

        if (query.Length == 0)
        {
            return [];
        }

        blacklist ??= Tools.FastContextToolHandler.DefaultBlacklistedWords;

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Always include the full query string for exact matches:
            query
        };

        // Scan character-by-character to extract individual words:
        var wordStart = -1;
        for (var queryTextIndex = 0; queryTextIndex <= query.Length; queryTextIndex++)
        {
            var isLetterOrDigit = queryTextIndex < query.Length && char.IsLetterOrDigit(query[queryTextIndex]);

            // ReSharper disable once ConvertIfStatementToSwitchStatement
            if (isLetterOrDigit && wordStart < 0)
            {
                // Start of a new word:
                wordStart = queryTextIndex;
            }
            else if (!isLetterOrDigit && wordStart >= 0)
            {
                // End of the current word.
                // Add it if it's long enough and not an blacklisted word:
                if (queryTextIndex - wordStart >= 3)
                {
                    var word = query[wordStart..queryTextIndex];

                    if (!blacklist.Contains(word))
                    {
                        tokens.Add(word);
                    }
                }

                wordStart = -1;
            }
        }

        return tokens.ToArray();
    }

    /// <summary>
    ///     Searches <paramref name="nodeText"/> line-by-line for occurrences of <paramref name="tokens"/>.
    ///     For each match, captures a small window of surrounding text.
    ///     Overlapping windows on the same line are merged, then snippets are printed with their document offsets.
    ///     Regions are selected greedily to maximize token coverage so each token gets at least one representation where possible.
    /// </summary>
    internal static void AppendSnippets(
        StringBuilder sb,
        string nodeText,
        int nodeStartOffset,
        IReadOnlyList<string> tokens,
        int snippetLength,
        int maxWindows,
        int desiredSnippets)
    {
        if (nodeText.Length == 0 || tokens.Count == 0)
        {
            return;
        }

        // Per-token tracking for round-robin matching so no single frequent token hogs the entire budget:
        var searchIndices = new int[tokens.Count];
        var exhausted = new bool[tokens.Count];

        var maxSnippets = Math.Max(tokens.Count, desiredSnippets);

        var windows = new List<(int Start, int End, int TokenIndex)>();

        // Scan the node line-by-line and collect match windows:
        var lineStart = 0;
        for (var textIndex = 0; textIndex <= nodeText.Length; textIndex++)
        {
            // Only stop at line boundaries:
            if (textIndex != nodeText.Length && nodeText[textIndex] != '\n')
            {
                continue;
            }

            var lineEnd = textIndex;

            if (lineEnd > lineStart && nodeText[lineEnd - 1] == '\r')
            {
                lineEnd--;
            }

            var lineLength = lineEnd - lineStart;
            if (lineLength > 0)
            {
                searchIndices.AsSpan().Clear();
                exhausted.AsSpan().Clear();

                // One match per token per pass until the line is exhausted:
                while (windows.Count < maxWindows)
                {
                    var anyFound = false;

                    for (var tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
                    {
                        if (exhausted[tokenIndex])
                        {
                            continue;
                        }

                        var token = tokens[tokenIndex];
                        var searchFrom = lineStart + searchIndices[tokenIndex];
                        var searchLength = lineLength - searchIndices[tokenIndex];

                        // Remaining line fragment is too short to hold this token:
                        if (searchLength < token.Length)
                        {
                            exhausted[tokenIndex] = true;
                            continue;
                        }

                        var indexOfToken = nodeText.IndexOf(
                            token,
                            searchFrom,
                            searchLength,
                            StringComparison.OrdinalIgnoreCase
                        );

                        if (indexOfToken != -1)
                        {
                            // Build a window around the match, clamped to the current line:
                            var matchPosInLine = indexOfToken - lineStart;
                            var windowStart = Math.Max(0, matchPosInLine - snippetLength);
                            var windowEnd = Math.Min(lineLength, matchPosInLine + token.Length + snippetLength);

                            windows.Add((lineStart + windowStart, lineStart + windowEnd, tokenIndex));

                            // Advance past this match so the next pass finds the next occurrence:
                            searchIndices[tokenIndex] = matchPosInLine + token.Length;
                            anyFound = true;

                            if (windows.Count >= maxWindows)
                            {
                                break;
                            }
                        }
                        else
                        {
                            exhausted[tokenIndex] = true;
                        }
                    }

                    if (!anyFound)
                    {
                        break;
                    }
                }
            }

            lineStart = textIndex + 1;

            if (windows.Count >= maxWindows)
            {
                break;
            }
        }

        if (windows.Count == 0)
        {
            return;
        }

        // Merge overlapping windows and union the tokens they represent:
        windows.Sort((a, b) => a.Start.CompareTo(b.Start));

        var merged = new List<(int Start, int End, HashSet<int> TokenIndices)>();
        var (matchStart, matchEnd, matchTokens) =
            (windows[0].Start, windows[0].End, new HashSet<int> { windows[0].TokenIndex });
        for (var windowIndex = 1; windowIndex < windows.Count; windowIndex++)
        {
            var (windowStart, windowEnd, windowToken) = windows[windowIndex];

            if (windowStart <= matchEnd)
            {
                // Overlapping or adjacent. Extend the current merged range:
                matchEnd = Math.Max(matchEnd, windowEnd);
                matchTokens.Add(windowToken);
            }
            else
            {
                merged.Add((matchStart, matchEnd, matchTokens));
                (matchStart, matchEnd, matchTokens) = (windowStart, windowEnd, [windowToken]);
            }
        }

        merged.Add((matchStart, matchEnd, matchTokens));

        // Print up to maxSnippets, preferring regions that cover the most uncovered tokens:
        var coveredTokens = new HashSet<int>();
        var printed = 0;
        while (printed < maxSnippets && merged.Count > 0)
        {
            // Find the region with the most uncovered tokens. Tie-break by earliest start:
            var bestIndex = 0;
            var bestNewTokens = -1;
            for (var i = 0; i < merged.Count; i++)
            {
                var newTokenCount = 0;
                foreach (var t in merged[i].TokenIndices)
                {
                    if (!coveredTokens.Contains(t))
                    {
                        newTokenCount++;
                    }
                }

                if (newTokenCount > bestNewTokens)
                {
                    bestNewTokens = newTokenCount;
                    bestIndex = i;
                }
                else if (newTokenCount == bestNewTokens && newTokenCount >= 0)
                {
                    if (merged[i].Start < merged[bestIndex].Start)
                    {
                        bestIndex = i;
                    }
                }
            }

            // No region adds any new token:
            if (bestNewTokens <= 0)
            {
                break;
            }

            var region = merged[bestIndex];

            var start = region.Start;
            var end = region.End;

            // Snap to nearest word boundaries for clean output:
            while (start > 0 && !char.IsWhiteSpace(nodeText[start]))
            {
                start--;
            }

            while (end < nodeText.Length && !char.IsWhiteSpace(nodeText[end]))
            {
                end++;
            }

            while (start < end && char.IsWhiteSpace(nodeText[start]))
            {
                start++;
            }

            while (end > start && char.IsWhiteSpace(nodeText[end - 1]))
            {
                end--;
            }

            var snippet = nodeText[start..end];
            var prefix = start > 0 ? "…" : "";
            var suffix = end < nodeText.Length ? "…" : "";

            // Convert back to document offsets for fetch:
            var docStart = nodeStartOffset + start;
            var docEnd = nodeStartOffset + end;

            sb.AppendLine($"      '{prefix}{snippet}{suffix}':{docStart},{docEnd}");
            printed++;

            coveredTokens.UnionWith(region.TokenIndices);
            merged.RemoveAt(bestIndex);
        }
    }
}