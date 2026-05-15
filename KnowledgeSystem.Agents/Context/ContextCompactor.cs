using System.Text;
using KnowledgeSystem.Agents.Context.TokenEstimation;
using OpenAI.Chat;

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
    ChatClient? summaryClient = null,
    string? summaryPrompt = null,
    int toolResultCharThreshold = 2000,
    int maxContextTokens = 16000,
    int preservedRecentTurns = 4
)
{
    /// <summary>
    ///     Compacts the context if needed. Call this after each agent run completes.
    /// </summary>
    public async Task CompactAsync(AgentContext context, CancellationToken cancellationToken = default)
    {
        CompactToolResults(context);

        if (summaryClient != null)
        {
            await SummarizeOldTurnsAsync(context, cancellationToken);
        }
    }

    /// <summary>
    ///     Replaces tool results exceeding the character threshold with compact stubs.
    ///     Only compacts results already "consumed" by a subsequent assistant message.
    /// </summary>
    internal void CompactToolResults(AgentContext context)
    {
        var elements = context.MutableElements;

        for (var i = 0; i < elements.Count; i++)
        {
            if (elements[i] is not ChatElement { Message: ToolChatMessage toolMsg })
            {
                continue;
            }
            
            if (toolMsg.Content.FirstOrDefault()?.Text?.Length <= toolResultCharThreshold)
            {
                continue;
            }

            // Only compact if an assistant message follows (LLM already used this result):
            var consumed = false;
            for (var j = i + 1; j < elements.Count; j++)
            {
                if (elements[j] is ChatElement { Message: AssistantChatMessage })
                {
                    consumed = true;
                    break;
                }
            }

            if (!consumed)
            {
                continue;
            }

            var originalText = toolMsg.Content.First().Text;
            var stub = CompactToolResultText(originalText);
            elements[i] = new ChatElement(new ToolChatMessage(toolMsg.ToolCallId, stub));
        }
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
        while (systemEnd < elements.Count && elements[systemEnd] is ChatElement { Message: SystemChatMessage })
        {
            systemEnd++;
        }

        // Find the start of recent turns to keep verbatim:
        var recentStart = elements.Count;
        var turnsFound = 0;
        for (var i = elements.Count - 1; i >= systemEnd && turnsFound < preservedRecentTurns; i--)
        {
            if (elements[i] is ChatElement { Message: AssistantChatMessage or UserChatMessage })
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
        elements.Insert(systemEnd, new ChatElement(new SystemChatMessage(summaryMessage)));
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

        var options = new ChatCompletionOptions();
        var result = await summaryClient!.CompleteChatAsync([new UserChatMessage(prompt)], options, cancellationToken);
        return result.Value.Content.FirstOrDefault()?.Text ?? "[Summary unavailable]";
    }
}
