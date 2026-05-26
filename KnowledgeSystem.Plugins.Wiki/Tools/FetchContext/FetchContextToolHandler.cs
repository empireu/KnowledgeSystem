using System.Text;
using KnowledgeSystem.Agent.Tools.Markers;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.EmdParser.MarkdownTree;
using KnowledgeSystem.Retrieval.Api;
using KnowledgeSystem.Retrieval.Api.Store;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Agent.Tools.FetchContext;

public sealed class FetchContextToolHandler(
    AgentTool tool,
    ArrayArgument referencesArgument,
    IReadOnlyDocumentStore store,
    FetchContextToolConfig config
) : ToolHandler<ConversationalContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<ConversationalContext> registry, IServiceProvider serviceProvider, FetchContextToolConfig config)
    {
        var fetchTool = new ToolBuilder("fetch_context")
            .WithDescription("Fetches full surrounding context for one or more references. Expands each offset to its containing paragraph or section, shows the heading path, and deduplicates overlapping ranges. Prefer this over repo_fetch when you have multiple references or need expanded context.")
            .WithRequiredArrayArgument("references", "Array of references to fetch context for. Formats: 'path/to/file.md:start,end' (offsets from fast_context/grep_content), 'path/to/file.md@Heading' (section), or 'path/to/file.md' (full file). Pass multiple to batch-fetch.", out var refsArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<FetchContextToolHandler>(serviceProvider, fetchTool, refsArg, config);
        registry.RegisterTool(fetchTool, handler);
    }

    public override Task<ToolExecutionResult> ExecuteAsync(AgentRunner<ConversationalContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var rawRefs = referencesArgument.GetValue(args);

        if (rawRefs.Length == 0)
        {
            return Task.FromResult(Error("fetch_context: Empty references array!"));
        }

        if (rawRefs.Length > config.MaxRefs)
        {
            return Task.FromResult(Error($"fetch_context: Too many references ({rawRefs.Length}, max {config.MaxRefs}). Batch in smaller groups."));
        }

        // Parse all references:
        var parsedRefs = new List<(EmdReferencePath Ref, string Raw)>();
        foreach (var raw in rawRefs)
        {
            if (!EmdReferencePath.TryParse(raw, out var refPath))
            {
                return Task.FromResult(Error($"fetch_context: Malformed reference '{raw}'. Use formats: 'file.md:start,end', 'file.md@Heading', or 'file.md'."));
            }

            parsedRefs.Add((refPath, raw));
        }

        var repo = store;
        var totalChars = 0;
        var sb = new StringBuilder();
        var fetchedDocumentData = new List<(EmdDocument Document, List<(int Start, int End)> Ranges, string Content)>();

        // Group by document to batch context per file:
        var documentGroups = parsedRefs
            .Select(p => (p.Ref.GetFile(), p.Ref, p.Raw))
            .GroupBy(x => x.Item1)
            .ToList();

        foreach (var group in documentGroups)
        {
            if (totalChars >= config.MaxChars)
            {
                sb.AppendLine("... (truncated, too many results)");
                break;
            }

            var fileKey = group.Key;

            if (!store.TryGetDocumentByPath(fileKey.RepositoryRelativePath, out var document))
            {
                sb.AppendLine($"fetch_context: Document '{fileKey.RepositoryRelativePath}' not found.");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine($"# Document: {document.Path}");
            sb.AppendLine();

            var documentSb = new StringBuilder();
            documentSb.AppendLine($"# Document: {document.Path}");
            documentSb.AppendLine();

            // Collect content ranges to fetch, deduplicating and merging overlaps:
            var ranges = new List<(int Start, int End, string Label)>();

            foreach (var (_, refPath, raw) in group)
            {
                switch (refPath.Type)
                {
                    case EmdReferencePath.ReferenceType.Offsets:
                    {
                        var containingNode = FindBlockNode(document, refPath.StartOffset, refPath.EndOffset);
                        if (containingNode != null)
                        {
                            var headingPath = GetHeadingPath(containingNode);
                            var label = string.IsNullOrEmpty(headingPath) ? raw : $"{raw} (under {headingPath})";
                            ranges.Add((containingNode.StartOffset, containingNode.EndOffset, label));
                        }
                        else
                        {
                            // Fallback: just use the exact offsets with slight expansion:
                            var expandStart = Math.Max(0, refPath.StartOffset - 100);
                            var expandEnd = Math.Min(document.Content.Length, refPath.EndOffset + 100);
                            ranges.Add((expandStart, expandEnd, raw));
                        }

                        break;
                    }
                    case EmdReferencePath.ReferenceType.Definition:
                    {
                        if (document.NodesWithDefinition.TryGetValue(refPath, out var defNode))
                        {
                            var headingPath = GetHeadingPath(defNode.RawNode);
                            var label = string.IsNullOrEmpty(headingPath) ? raw : $"{raw} (under {headingPath})";
                            ranges.Add((defNode.RawNode.StartOffset, defNode.RawNode.EndOffset, label));
                        }
                        else
                        {
                            sb.AppendLine($"  Definition '{refPath.Definition}' not found in this file.");
                            documentSb.AppendLine($"  Definition '{refPath.Definition}' not found in this file.");
                            sb.AppendLine();
                            documentSb.AppendLine();
                        }

                        break;
                    }
                    case EmdReferencePath.ReferenceType.File:
                    {
                        ranges.Add((0, document.Content.Length, raw));
                        break;
                    }
                    case EmdReferencePath.ReferenceType.Directory:
                    {
                        sb.AppendLine($"  Cannot fetch context for directory '{raw}'.");
                        documentSb.AppendLine($"  Cannot fetch context for directory '{raw}'.");
                        sb.AppendLine();
                        documentSb.AppendLine();
                        break;
                    }
                }
            }

            if (ranges.Count == 0)
            {
                continue;
            }

            // Sort by start offset, then merge overlapping/adjacent ranges:
            ranges.Sort((a, b) => a.Start.CompareTo(b.Start));

            var merged = new List<(int Start, int End, List<string> Labels)>();
            var (currentStart, currentEnd, currentLabels) = (ranges[0].Start, ranges[0].End, new List<string> { ranges[0].Label });

            for (var i = 1; i < ranges.Count; i++)
            {
                var (start, end, label) = ranges[i];

                // Overlapping or adjacent (within 10 chars):
                if (start <= currentEnd + 10)
                {
                    currentEnd = Math.Max(currentEnd, end);
                    currentLabels.Add(label);
                }
                else
                {
                    merged.Add((currentStart, currentEnd, currentLabels));
                    (currentStart, currentEnd, currentLabels) = (start, end, [label]);
                }
            }

            merged.Add((currentStart, currentEnd, currentLabels));

            var documentFetchedRanges = new List<(int Start, int End)>();

            // Append each merged range:
            foreach (var (start, end, labels) in merged)
            {
                if (totalChars >= config.MaxChars)
                {
                    sb.AppendLine("... (truncated)");
                    documentSb.AppendLine("... (truncated)");
                    break;
                }

                var content = document.Content[start..end];

                if (totalChars + content.Length > config.MaxChars)
                {
                    // Truncate this block:
                    var available = config.MaxChars - totalChars;
                    content = content[..available] + "... (truncated)";
                }

                // Show which references are covered by this block:
                var labelStr = labels.Count == 1
                    ? labels[0] 
                    : $"[{string.Join(", ", labels)}]";
                
                sb.AppendLine($"  {start},{end} — {labelStr}");
                documentSb.AppendLine($"  {start},{end} — {labelStr}");
                sb.AppendLine();
                documentSb.AppendLine();

                // Indent the content slightly for readability:
                foreach (var line in content.Split('\n'))
                {
                    sb.AppendLine($"  {line}");
                    documentSb.AppendLine($"  {line}");
                }

                sb.AppendLine();
                documentSb.AppendLine();
                totalChars += content.Length;
                documentFetchedRanges.Add((start, end));
            }

            if (documentFetchedRanges.Count > 0)
            {
                fetchedDocumentData.Add((document, documentFetchedRanges, documentSb.ToString()));
            }
        }

        if (sb.Length == 0)
        {
            return Task.FromResult(Error("fetch_context: No content could be fetched from the given references."));
        }

        var result = sb.ToString();

        foreach (var (document, ranges, content) in fetchedDocumentData)
        {
            runner.ExecutionContext.ChatContext.InsertElement(new RepositoryFetchedTextMarker
            {
                Document = document,
                FetchedRanges = ranges,
                Content = content
            });
        }

        return Task.FromResult(Success(result));
    }

    /// <summary>
    ///     Finds the smallest block-level node containing the given offset range.
    ///     Walks down to the deepest containing node, then walks up to a block boundary
    ///     (Paragraph, ListItem, CodeBlock, or any Heading).
    /// </summary>
    private static MarkdownNode? FindBlockNode(EmdDocument document, int startOffset, int endOffset)
    {
        return FindBlockNodeInner(document.RootNode.RawNode, startOffset, endOffset);
    }

    private static MarkdownNode? FindBlockNodeInner(MarkdownNode node, int startOffset, int endOffset)
    {
        // Check if this node contains the range:
        if (startOffset < node.StartOffset || endOffset > node.EndOffset)
        {
            return null;
        }

        // Try to find a deeper child that contains the range:
        foreach (var child in node.Children)
        {
            var found = FindBlockNodeInner(child, startOffset, endOffset);
            if (found != null)
            {
                return found;
            }
        }

        // No child fully contains the range. This is the deepest containing node.
        // Walk up to the nearest block-level ancestor:
        var current = node;
        while (current.Parent != null)
        {
            var type = current.NodeType;
            if (type == MarkdownNode.Type.Paragraph ||
                type == MarkdownNode.Type.ListItem ||
                type == MarkdownNode.Type.CodeBlock ||
                type == MarkdownNode.Type.Blockquote ||
                type.IsHeading())
            {
                return current;
            }

            current = current.Parent;
        }

        // Fallback: return the root document node's range (whole file)
        return node;
    }

    /// <summary>
    ///     Builds the heading path for a node, starting from the topmost heading ancestor.
    /// </summary>
    private static string GetHeadingPath(MarkdownNode node)
    {
        var headings = new List<string>();
        var current = node;

        while (current != null)
        {
            if (current.NodeType.IsHeading())
            {
                headings.Add(current.Text);
            }

            current = current.Parent;
        }

        // Reverse so topmost heading comes first:
        headings.Reverse();
        return string.Join(" > ", headings);
    }
}
