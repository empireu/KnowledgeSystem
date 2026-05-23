using System.Text;
using KnowledgeSystem.Agents.Context.TokenEstimation;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Agents.Context;

/// <summary>
///     Compacts the chat context to keep token usage bounded.
///     Two strategies applied in order:
///     <list type="number">
///         <item>Tool result compaction: replaces large tool results with stubs after the LLM has used them.</item>
///         <item>Conversation summarization: summarizes older turns when total tokens exceed a threshold.</item>
///     </list>
/// </summary>
public sealed class ContextCompactor(
    ITokenEstimator tokenEstimator,
    IChatClient? summaryClient = null,
    string? summaryPrompt = null,
    int toolResultCharThreshold = 2000,
    int maxContextTokens = 16000,
    int preservedRecentTurns = 4
)
{
    public readonly struct CompactionInfo
    {
        public required int ToolsCompacted { get; init; }
        
        public required bool InvokedSummarizer { get; init; }
    }
    
    /// <summary>
    ///     Compacts the context if needed. Call this after each agent run completes.
    /// </summary>
    public async Task<CompactionInfo> CompactAsync(AgentContext context, CancellationToken cancellationToken = default)
    {
        var toolsCompacted = CompactToolResults(context);

        var invokedSummary = false;
        if (summaryClient != null)
        {
            await SummarizeOldTurnsAsync(context, cancellationToken);
            invokedSummary = true;
        }

        return new CompactionInfo
        {
            ToolsCompacted = toolsCompacted,
            InvokedSummarizer = invokedSummary
        };
    }

    /// <summary>
    ///     Replaces tool results exceeding the character threshold with compact stubs.
    ///     Only compacts results already "consumed" by a subsequent assistant message.
    /// </summary>
    internal int CompactToolResults(AgentContext context)
    {
        var elements = context.MutableElements;

        var toolsCompacted = 0;
        for (var i = 0; i < elements.Count; i++)
        {
            if (elements[i] is not ChatElement toolElement || toolElement.Message.Role != ChatRole.Tool)
            {
                continue;
            }

            if (!ChatMessageHelpers.TryGetToolResultText(toolElement.Message, out var originalText, out var callId) || originalText.Length <= toolResultCharThreshold)
            {
                continue;
            }

            // Only compact if an assistant message follows (LLM already used this result):
            var consumed = false;
            for (var j = i + 1; j < elements.Count; j++)
            {
                if (elements[j] is ChatElement { Message: var msg } && msg.Role == ChatRole.Assistant)
                {
                    consumed = true;
                    break;
                }
            }

            if (!consumed)
            {
                continue;
            }

            var stub = CompactToolResultText(originalText);
            
            var replacementContents = new List<AIContent>();
            if (callId != null)
            {
                replacementContents.Add(new FunctionResultContent(callId, stub));
            }
            else
            {
                replacementContents.Add(new TextContent(stub));
            }
            
            elements[i] = new ChatElement(new ChatMessage(ChatRole.Tool, replacementContents)
            {
                AuthorName = toolElement.Message.AuthorName
            });
            
            ++toolsCompacted;
        }

        return toolsCompacted;
    }

    /// <summary>
    ///     Summarizes older turns when total tokens exceed the limit.
    ///     Preserves system messages and the most recent turns verbatim.
    /// </summary>
    private async Task SummarizeOldTurnsAsync(AgentContext context, CancellationToken cancellationToken)
    {
        var tokenCount = tokenEstimator.CountTokens(context.ChatMessages);
        
        if (tokenCount <= maxContextTokens)
        {
            return;
        }

        var elements = context.MutableElements;

        // Preserve leading system messages:
        var systemEnd = 0;
        while (systemEnd < elements.Count && elements[systemEnd] is ChatElement { Message: var sysMsg } && sysMsg.Role == ChatRole.System)
        {
            systemEnd++;
        }

        // Find the start of recent turns to keep verbatim:
        var recentStart = elements.Count;
        var turnsFound = 0;
        for (var i = elements.Count - 1; i >= systemEnd && turnsFound < preservedRecentTurns; i--)
        {
            if (elements[i] is ChatElement { Message: var turnMsg } && (turnMsg.Role == ChatRole.Assistant || turnMsg.Role == ChatRole.User))
            {
                turnsFound++;
                recentStart = i;
            }
        }

        if (recentStart <= systemEnd)
        {
            return;
        }

        // Extract old turns as text:
        var oldTurnsText = new StringBuilder();
        for (var i = systemEnd; i < recentStart; i++)
        {
            if (elements[i] is ChatElement chat)
            {
                oldTurnsText.AppendLine(chat.ToLogFormat());
            }
        }

        var summary = await GenerateSummaryAsync(oldTurnsText.ToString(), cancellationToken);

        // Replace old turns with a single system message:
        var summaryMessage = $"[Earlier conversation summary]\n{summary}";
        var removeCount = recentStart - systemEnd;
        elements.RemoveRange(systemEnd, removeCount);
        elements.Insert(systemEnd, new ChatElement(new ChatMessage(ChatRole.System, summaryMessage)));
    }

    private static string CompactToolResultText(string original)
    {
        var firstLineEnd = original.IndexOf('\n');
        var header = firstLineEnd > 0 ? original[..firstLineEnd].Trim() : null;

        if (header != null && header.StartsWith('#'))
        {
            return $"{header} [{original.Length} chars compacted]";
        }

        return $"[Tool result compacted: {original.Length} chars]";
    }

    private async Task<string> GenerateSummaryAsync(string conversationText, CancellationToken cancellationToken)
    {
        var prompt = summaryPrompt ?? $"""
            Summarize the following conversation between a user and an AI assistant.
            Preserve the key facts, decisions, and conclusions. Be concise.

            {conversationText}
            """;

        var response = await summaryClient!.GetResponseAsync([new ChatMessage(ChatRole.User, prompt)], cancellationToken: cancellationToken);
        return response.Text ?? "[Summary unavailable]";
    }
}
