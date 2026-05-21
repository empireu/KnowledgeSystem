using System.Text;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Agents.Context;

/// <summary>
///     Wraps an AI message, that gets sent to the LLM.
/// </summary>
public sealed class ChatElement(ChatMessage message) : ITimelineElement
{
    public readonly ChatMessage Message = message;

    public string ToLogFormat()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"{Message.Role}:");

        if (Message.AuthorName != null)
        {
            sb.AppendLine($"  Name: {Message.AuthorName}");
        }

        // Check for tool result content:
        foreach (var content in Message.Contents)
        {
            switch (content)
            {
                case FunctionResultContent resultContent:
                {
                    sb.AppendLine($"  ToolCallId: {resultContent.CallId}");
                    
                    if (ChatMessageHelpers.FormatContentValue(resultContent.Result) is { } resultText)
                    {
                        sb.AppendLine($"  Result: {resultText}");
                    }
                    
                    break;
                }
                case TextContent textContent:
                {
                    foreach (var line in textContent.Text.EnumerateLines())
                    {
                        sb.Append("  ");
                        sb.Append(line);
                        sb.AppendLine();
                    }
                
                    break;
                }
                default:
                    sb.AppendLine($"  [{content.GetType().Name}] (unknown)");
                    break;
            }
        }

        // Check for function call content (tool calls from assistant)
        foreach (var content in Message.Contents)
        {
            if (content is FunctionCallContent toolCall)
            {
                sb.AppendLine($"  [ToolCall] {toolCall.Name}({toolCall.Arguments})");
            }
        }

        return sb.ToString();
    }
}
