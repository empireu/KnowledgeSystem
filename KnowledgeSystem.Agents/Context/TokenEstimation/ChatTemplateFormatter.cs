using System.Text;
using OpenAI.Chat;

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

    private static string GetChatMlRole(ChatMessage message) => message switch
    {
        SystemChatMessage => "system",
        DeveloperChatMessage => "developer",
        UserChatMessage => "user",
        AssistantChatMessage => "assistant",
        ToolChatMessage => "tool",
        _ => throw new ArgumentOutOfRangeException(nameof(message), message.GetType().Name, null)
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

    private static string GetGemmaRole(ChatMessage message) => message switch
    {
        SystemChatMessage => "system",
        DeveloperChatMessage => "system",
        UserChatMessage => "user",
        AssistantChatMessage => "model",
        ToolChatMessage => "user",
        _ => throw new ArgumentOutOfRangeException(nameof(message), message.GetType().Name, null)
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

    private static string GetGlmRole(ChatMessage message) => message switch
    {
        SystemChatMessage => "system",
        DeveloperChatMessage => "system",
        UserChatMessage => "user",
        AssistantChatMessage => "assistant",
        ToolChatMessage => "observation",
        _ => throw new ArgumentOutOfRangeException(nameof(message), message.GetType().Name, null)
    };
    
    private static void AppendContentEstimate(StringBuilder sb, ChatMessage message)
    {
        foreach (var part in message.Content)
        {
            if (part.Kind == ChatMessageContentPartKind.Text)
            {
                sb.AppendLine(part.Text);
            }
        }

        if (message is AssistantChatMessage { ToolCalls.Count: > 0 } assistant)
        {
            foreach (var toolCall in assistant.ToolCalls)
            {
                sb.Append(toolCall.FunctionName);
                sb.Append('(');
                sb.Append(toolCall.FunctionArguments);
                sb.Append(')');
                sb.AppendLine();
            }
        }
    }
}