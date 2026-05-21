using System.Text;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Agents.Context.TokenEstimation;

internal sealed class ChatTemplateFormatter
{
    private readonly Func<IEnumerable<ChatMessage>, string> _format;

    private ChatTemplateFormatter(Func<IEnumerable<ChatMessage>, string> format)
    {
        _format = format;
    }

    public string Format(IEnumerable<ChatMessage> messages) => _format(messages);

    public static ChatTemplateFormatter Create(ChatTemplateFormat format, SpecialTokens tokens) => format switch
    {
        ChatTemplateFormat.ChatMl => ChatMl(),
        ChatTemplateFormat.Gemma => Gemma(tokens),
        ChatTemplateFormat.Glm => Glm(tokens),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
    };
    
    /// <summary>
    ///     ChatML approximation.
    /// </summary>
    public static ChatTemplateFormatter ChatMl()
    {
        return new ChatTemplateFormatter(messages =>
        {
            var sb = new StringBuilder();
            foreach (var msg in messages)
            {
                sb.Append("<|im_start|>");
                sb.Append(GetChatMlRole(msg));
                sb.Append('\n');
                AppendContentEstimate(sb, msg);
                sb.Append("<|im_end|>\n");
            }

            sb.Append("<|im_start|>assistant\n");
            return sb.ToString();
        });
    }

    private static string GetChatMlRole(ChatMessage message) => message.Role.Value switch
    {
        "system" => "system",
        "developer" => "developer",
        "user" => "user",
        "assistant" => "assistant",
        "tool" => "tool",
        _ => message.Role.Value
    };

    /// <summary>
    ///     Gemma-4 approximation.
    /// </summary>
    private static ChatTemplateFormatter Gemma(SpecialTokens tokens)
    {
        return new ChatTemplateFormatter(messages =>
        {
            var sb = new StringBuilder();
            sb.Append(tokens.BosToken);

            foreach (var msg in messages)
            {
                sb.Append(tokens.StartTurnToken);
                sb.Append(GetGemmaRole(msg));
                sb.Append('\n');
                AppendContentEstimate(sb, msg);
                sb.Append(tokens.EndTurnToken);
                sb.Append('\n');
            }

            sb.Append(tokens.StartTurnToken);
            sb.Append("model\n");
            return sb.ToString();
        });
    }

    private static string GetGemmaRole(ChatMessage message) => message.Role.Value switch
    {
        "system" => "system",
        "developer" => "system",
        "user" => "user",
        "assistant" => "model",
        "tool" => "user",
        _ => message.Role.Value
    };

    /// <summary>
    ///     GLM-4 approximation.
    /// </summary>
    private static ChatTemplateFormatter Glm(SpecialTokens tokens)
    {
        return new ChatTemplateFormatter(messages =>
        {
            var sb = new StringBuilder();
            sb.Append(tokens.BosToken);
            sb.Append(' ');

            foreach (var msg in messages)
            {
                sb.Append("<|");
                sb.Append(GetGlmRole(msg));
                sb.Append("|>");
                AppendContentEstimate(sb, msg);
            }

            sb.Append("<|assistant|>");
            return sb.ToString();
        });
    }

    private static string GetGlmRole(ChatMessage message) => message.Role.Value switch
    {
        "system" => "system",
        "developer" => "system",
        "user" => "user",
        "assistant" => "assistant",
        "tool" => "observation",
        _ => message.Role.Value
    };
    
    private static void AppendContentEstimate(StringBuilder sb, ChatMessage message)
    {
        foreach (var text in ChatMessageHelpers.EnumerateTextContents(message))
        {
            sb.AppendLine(text);
        }

        // Append function call info for token estimation:
        foreach (var content in message.Contents)
        {
            if (content is FunctionCallContent toolCall)
            {
                sb.Append(toolCall.Name);
                sb.Append('(');
                if (toolCall.Arguments != null)
                {
                    foreach (var kvp in toolCall.Arguments)
                    {
                        sb.Append(kvp.Key);
                        sb.Append(':');
                        sb.Append(kvp.Value);
                    }
                }
                sb.Append(')');
                sb.AppendLine();
            }
        }
    }
}
