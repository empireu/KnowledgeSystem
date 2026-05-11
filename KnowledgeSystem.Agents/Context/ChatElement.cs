using System.Text;
using OpenAI.Chat;

namespace KnowledgeSystem.Agents.Context;

/// <summary>
///     Wraps an OpenAI message, that gets sent to the LLM.
/// </summary>
public sealed class ChatElement(ChatMessage message) : ITimelineElement
{
    public readonly ChatMessage Message = message;

    public string ToLogFormat()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"{Message.GetType().Name}:");

        if (Message is AssistantChatMessage { ParticipantName: not null } assistant)
        {
            sb.AppendLine($"  Name: {assistant.ParticipantName}");
        }
        
        if (Message is SystemChatMessage { ParticipantName: not null } system)
        {
            sb.AppendLine($"  Name: {system.ParticipantName}");
        }

        if (Message is ToolChatMessage tool)
        {
            sb.AppendLine($"  ToolCallId: {tool.ToolCallId}");
        }

        foreach (var part in Message.Content)
        {
            switch (part.Kind)
            {
                case ChatMessageContentPartKind.Text:
                {
                    foreach (var line in part.Text.EnumerateLines())
                    {
                        sb.Append("  ");
                        sb.Append(line);
                        sb.AppendLine();
                    }

                    break;
                }
                case ChatMessageContentPartKind.Refusal:
                {
                    sb.AppendLine($"  [Refusal] {part.Refusal}");
                    break;
                }
                case ChatMessageContentPartKind.Image:
                {
                    if (part.ImageUri != null)
                    {
                        sb.AppendLine($"  [Image URI] {part.ImageUri}");
                    }
                    else if (part.ImageBytes != null)
                    {
                        sb.AppendLine($"  [Image Bytes] {part.ImageBytesMediaType}, {part.ImageBytes.Length} bytes");
                    }

                    break;
                }
                default:
                    sb.AppendLine($"  [{part.Kind}] (unknown)");
                    break;
            }
        }

        if (Message is AssistantChatMessage { ToolCalls.Count: > 0 } assistantWithTools)
        {
            foreach (var toolCall in assistantWithTools.ToolCalls)
            {
                sb.AppendLine($"  [ToolCall] {toolCall.FunctionName}({toolCall.FunctionArguments})");
            }
        }

        return sb.ToString();
    }
}