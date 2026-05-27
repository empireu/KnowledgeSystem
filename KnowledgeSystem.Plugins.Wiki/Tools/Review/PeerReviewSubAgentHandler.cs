using System.Text;
using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Ai;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.EmdParser.MarkdownTree;
using KnowledgeSystem.Events.Implementation;
using KnowledgeSystem.Plugins.Library;
using KnowledgeSystem.Plugins.Wiki.Tools.Markers;
using Microsoft.Extensions.AI;

// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Plugins.Wiki.Tools.Review;

public class PeerReviewSubAgentHandler(
    AgentTool tool,
    StringArgument reportArgument,
    ProviderConfig reviewProvider,
    ChatOptionsConfig reviewChatOptions,
    string reviewSystemPromptFile
) : ToolHandler<ConversationalContext>.SubAgent(tool) {
    public const string ToolId = "submit_with_review";    
    
    public static void Register(AgentToolRegistry<ConversationalContext> registry, ProviderConfig reviewProvider, ChatOptionsConfig reviewChatOptions, string reviewSystemPromptFile)
    {
        var reviewTool = new ToolBuilder(ToolId)
            .WithDescription("Submits your message for the user to be peer-reviewed. If it passes, it will be shown to the user immediately. Otherwise, you will get a report on the found issues. Only call if you are responding with any information; don't call if you are just exchanging pleasantries.")
            .WithRequiredStringArgument("report", "Your final report for the user.", out var reportArg)
            .Build();

        var handler = new PeerReviewSubAgentHandler(
            reviewTool,
            reportArg,
            reviewProvider,
            reviewChatOptions,
            reviewSystemPromptFile
        );
        
        registry.RegisterTool(reviewTool, handler);
    }
    
    public override async Task<ISubAgentProxy> BeginSubAgentExecution(
        AgentRunner<ConversationalContext> runner,
        ArgumentExtractionResult args, 
        ConversationalContext runContext,
        string toolCallId,
        CancellationToken cancellationToken)
    {
        var report = reportArgument.GetValue(args);

        if (string.IsNullOrWhiteSpace(report))
        {
            throw new Exception("Report is empty");
        }
        
        var systemPrompt = await File.ReadAllTextAsync(reviewSystemPromptFile, cancellationToken);
        
        var sb = new StringBuilder();
        sb.AppendLine(systemPrompt);

        var elements = runContext.ChatContext.MutableElements;
        
        // Distills the effective data used by the main agent:
        var startIndex = elements.FindLastIndex(element => element is ChatElement { Message: var msg } && msg.Role == ChatRole.User);

        if (startIndex == -1)
        {
            throw new Exception("Could not isolate user message");
        }

        var visitedNodes = new HashSet<MarkdownNode>();
        var emittedRangesByDoc = new Dictionary<EmdDocument, List<(int Start, int End)>>();
        
        // First pass: collect all fetched ranges (from node and text markers) for deduplication:
        var fetchedRangesByDocument = new Dictionary<EmdDocument, List<(int Start, int End)>>();
        for (var i = startIndex + 1; i < elements.Count; i++)
        {
            if (elements[i] is RepositoryFetchedNodeMarker nodeMarker)
            {
                if (!fetchedRangesByDocument.TryGetValue(nodeMarker.Node.Document, out var list))
                {
                    list = [];
                    fetchedRangesByDocument[nodeMarker.Node.Document] = list;
                }
                
                list.Add((nodeMarker.Node.RawNode.StartOffset, nodeMarker.Node.RawNode.EndOffset));
            }
            else if (elements[i] is RepositoryFetchedTextMarker textMarker)
            {
                if (!fetchedRangesByDocument.TryGetValue(textMarker.Document, out var list))
                {
                    list = [];
                    fetchedRangesByDocument[textMarker.Document] = list;
                }
                
                list.AddRange(textMarker.FetchedRanges);
            }
        }
        
        // Second pass: distill context:
        for (var i = startIndex + 1; i < elements.Count; i++)
        {
            var element = elements[i];

            switch (element)
            {
                case FastContextMarker fastContextMarker:
                {
                    if (fastContextMarker.Ranges.Count == 0)
                    {
                        // No search ranges, but output may contain gap token warnings or zero-result messages:
                        sb.AppendLine(fastContextMarker.Output);
                        sb.AppendLine("---");
                        break;
                    }
                    
                    // Only include ranges not already covered by fetched content:
                    var uncovered = FilterUncoveredRanges(fastContextMarker.Ranges, fetchedRangesByDocument);
                    if (uncovered.Count == 0)
                    {
                        break;
                    }
                    
                    if (uncovered.Count == fastContextMarker.Ranges.Count)
                    {
                        // Nothing was covered. Include the full formatted output:
                        sb.AppendLine(fastContextMarker.Output);
                    }
                    else
                    {
                        // Partial coverage. Include only the uncovered ranges:
                        foreach (var range in uncovered)
                        {
                            sb.AppendLine(range.Formatted);
                        }
                    }
                    
                    sb.AppendLine("---");
                    
                    break;
                }
                case RepositoryFetchedNodeMarker repoFetchNodeMarker:
                {
                    // Check if this node is a child of an already added node:
                    var isIncluded = false;
                    var current = repoFetchNodeMarker.Node.RawNode;
                    while (current != null)
                    {
                        if (visitedNodes.Contains(current))
                        {
                            isIncluded = true;
                            break;
                        }

                        current = current.Parent;
                    }

                    // Also check if the node's range is already covered by previously emitted text content:
                    if (!isIncluded && IsRangeCovered(emittedRangesByDoc, repoFetchNodeMarker.Node.Document, repoFetchNodeMarker.Node.RawNode.StartOffset, repoFetchNodeMarker.Node.RawNode.EndOffset))
                    {
                        isIncluded = true;
                    }

                    if (!isIncluded)
                    {
                        visitedNodes.Add(repoFetchNodeMarker.Node.RawNode);

                        var node = repoFetchNodeMarker.Node;

                        var content = node.Document.Content.Substring(
                            node.RawNode.StartOffset,
                            node.RawNode.EndOffset - node.RawNode.StartOffset
                        );

                        sb.AppendLine(content);
                        sb.AppendLine("---");

                        // Track the emitted range for text marker dedup:
                        if (!emittedRangesByDoc.TryGetValue(node.Document, out var emittedList))
                        {
                            emittedList = [];
                            emittedRangesByDoc[node.Document] = emittedList;
                        }
                        emittedList.Add((node.RawNode.StartOffset, node.RawNode.EndOffset));
                    }

                    break;
                }
                case RepositoryFetchedTextMarker repoFetchTextMarker:
                {
                    // Check if all ranges in this marker are already covered by previously emitted content:
                    var allCovered = true;
                    foreach (var (fStart, fEnd) in repoFetchTextMarker.FetchedRanges)
                    {
                        if (!IsRangeCovered(emittedRangesByDoc, repoFetchTextMarker.Document, fStart, fEnd))
                        {
                            allCovered = false;
                            break;
                        }
                    }

                    if (allCovered)
                    {
                        break;
                    }

                    sb.AppendLine(repoFetchTextMarker.Content);
                    sb.AppendLine("---");

                    // Track the emitted ranges for subsequent dedup:
                    if (!emittedRangesByDoc.TryGetValue(repoFetchTextMarker.Document, out var emitted))
                    {
                        emitted = [];
                        emittedRangesByDoc[repoFetchTextMarker.Document] = emitted;
                    }
                    
                    emitted.AddRange(repoFetchTextMarker.FetchedRanges);
                    break;
                }
                case GrepContentMarker grepContentMarker:
                {
                    var uncovered = FilterUncoveredRanges(grepContentMarker.Ranges, fetchedRangesByDocument);
                    if (uncovered.Count == 0)
                    {
                        break;
                    }
                    
                    if (uncovered.Count == grepContentMarker.Ranges.Count)
                    {
                        sb.AppendLine(grepContentMarker.Output);
                    }
                    else
                    {
                        foreach (var range in uncovered)
                        {
                            sb.AppendLine(range.Formatted);
                        }
                    }
                    
                    sb.AppendLine("---");
                    break;
                }
            }
        }
        
        var reviewContext = new PeerReviewContext(
            toolCallId, 
            sb.ToString(),
            report
        );
        
        return new Proxy(runner, this, reviewContext, reviewProvider, reviewChatOptions, cancellationToken);
    }
    
    /// <summary>
    ///     Checks whether a specific range in a document is fully covered by previously emitted ranges.
    ///     Merges the emitted ranges on-the-fly before checking containment.
    /// </summary>
    private static bool IsRangeCovered(
        Dictionary<EmdDocument, List<(int Start, int End)>> emittedRangesByDoc,
        EmdDocument document,
        int start,
        int end)
    {
        if (!emittedRangesByDoc.TryGetValue(document, out var ranges) || ranges.Count == 0)
        {
            return false;
        }

        // Merge and check:
        var sorted = ranges.OrderBy(r => r.Start).ToList();
        var merged = new List<(int Start, int End)> { sorted[0] };
        for (var i = 1; i < sorted.Count; i++)
        {
            var (s, e) = sorted[i];
            var last = merged[^1];
            if (s <= last.End)
            {
                merged[^1] = (last.Start, Math.Max(last.End, e));
            }
            else
            {
                merged.Add((s, e));
            }
        }

        for (var index = 0; index < merged.Count; index++)
        {
            var (mStart, mEnd) = merged[index];
            
            if (mStart <= start && mEnd >= end)
            {
                return true;
            }
        }

        return false;
    }
    
    /// <summary>
    ///     Filters <see cref="ContentRange"/> list to only include ranges not fully covered by fetched content.
    ///     A range is considered covered if the merged fetched ranges in the same document fully contain it.
    /// </summary>
    private static List<ContentRange> FilterUncoveredRanges(
        List<ContentRange> ranges,
        Dictionary<EmdDocument, List<(int Start, int End)>> fetchedRangesByDocument)
    {
        if (ranges.Count == 0 || fetchedRangesByDocument.Count == 0)
        {
            return ranges;
        }
        
        // Pre-merge fetched ranges per document for efficient containment checks:
        var mergedByDoc = new Dictionary<EmdDocument, List<(int Start, int End)>>();
        foreach (var (document, fetchedRanges) in fetchedRangesByDocument)
        {
            if (fetchedRanges.Count <= 1)
            {
                mergedByDoc[document] = fetchedRanges;
                continue;
            }
            
            var sorted = fetchedRanges.OrderBy(r => r.Start).ToList();
            var merged = new List<(int Start, int End)> { sorted[0] };
            for (var i = 1; i < sorted.Count; i++)
            {
                var (s, e) = sorted[i];
                var last = merged[^1];
                if (s <= last.End)
                {
                    merged[^1] = (last.Start, Math.Max(last.End, e));
                }
                else
                {
                    merged.Add((s, e));
                }
            }
            
            mergedByDoc[document] = merged;
        }
        
        var uncovered = new List<ContentRange>();
        for (var rangeIndex = 0; rangeIndex < ranges.Count; rangeIndex++)
        {
            var range = ranges[rangeIndex];
            
            if (!mergedByDoc.TryGetValue(range.Document, out var mergedRanges))
            {
                uncovered.Add(range);
                continue;
            }

            // A range is covered if any single merged fetched range fully contains it:
            var isCovered = false;
            for (var mergedRangeIndex = 0; mergedRangeIndex < mergedRanges.Count; mergedRangeIndex++)
            {
                var (fetchStart, fetchEnd) = mergedRanges[mergedRangeIndex];
                
                if (fetchStart <= range.StartOffset && fetchEnd >= range.EndOffset)
                {
                    isCovered = true;
                    break;
                }
            }

            if (!isCovered)
            {
                uncovered.Add(range);
            }
        }

        return uncovered;
    }
    
    public sealed class Proxy(
        AgentRunner<ConversationalContext> parentRunner,
        PeerReviewSubAgentHandler subAgentHandler,
        PeerReviewContext reviewContext,
        ProviderConfig reviewProvider,
        ChatOptionsConfig reviewChatOptions,
        CancellationToken cancellationToken
    ) : ISubAgentProxy
    {
        private const int MaxTurns = 7;

        public PeerReviewContext ReviewContext { get; } = reviewContext;

        private readonly AgentRunner<PeerReviewContext> _runner = new(
            OpenAiChatClientFactory.Create(reviewProvider),
            new PeerReviewAgent("peer_reviewer"),
            parentRunner,
            reviewContext,
            NullEventManager.Instance,
            cancellationToken,
            AgentRunner.ICompletionFactory.Wrap(reviewChatOptions.CreateOptions)
        );
        
        private int _turnCount;
        
        public AgentRunner AgentRunner  => _runner;
        
        public async Task<ToolExecutionResult?> StepAsync()
        {
            if (parentRunner.ActiveToolCalls.Count > 1)
            {
                return subAgentHandler.Error("Cannot execute review in parallel with other tools!");
            }
            
            if (++_turnCount > MaxTurns)
            {
                return subAgentHandler.Error($"Review agent exceeded maximum turn limit ({MaxTurns}).");
            }
            
            var result = await _runner.ExecuteTurn();

            if (result == AgentRunner.TurnStatus.CompletedSuccessfully)
            {
                switch (ReviewContext.FinalStatus)
                {
                    case PeerReviewContext.Status.Approved:
                        return subAgentHandler.Success("Review passed. Your report has been shown to the user.");
                    case PeerReviewContext.Status.Rejected:
                        return subAgentHandler.Error(ReviewContext.Feedback ?? "No feedback provided.");
                    case PeerReviewContext.Status.Invalid:
                    default:
                        return subAgentHandler.Error("Review agent completed without calling approve or reject.");
                }
            }

            if (result == AgentRunner.TurnStatus.CompletedWithError)
            {
                return subAgentHandler.Error($"Review agent error: {_runner.FinishError?.Message ?? "Unknown error"}");
            }

            return null;
        }
    }
}