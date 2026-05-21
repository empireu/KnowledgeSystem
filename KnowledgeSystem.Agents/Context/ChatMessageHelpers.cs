using System.Text.Json;
using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Agents.Context;

/// <summary>
///     Normalizes message/content access so SDK-specific chat content shapes are handled in one place.
/// </summary>
public static class ChatMessageHelpers
{
    public static List<FunctionCallContent> GetFunctionCalls(ChatResponse response)
    {
        var calls = new List<FunctionCallContent>();

        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent functionCall)
                {
                    calls.Add(functionCall);
                }
            }
        }

        return calls;
    }

    public static IEnumerable<string> EnumerateTextContents(ChatMessage message)
    {
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent textContent:
                    yield return textContent.Text;
                    break;
                case FunctionResultContent resultContent:
                    if (FormatContentValue(resultContent.Result) is { } resultText)
                    {
                        yield return resultText;
                    }

                    break;
            }
        }
    }

    public static bool TryGetToolResultText(ChatMessage message, out string text, out string? callId)
    {
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case FunctionResultContent resultContent:
                {
                    var formattedText = FormatContentValue(resultContent.Result);
                    if (formattedText != null)
                    {
                        text = formattedText;
                        callId = resultContent.CallId;
                        return true;
                    }

                    break;
                }
                case TextContent textContent:
                    text = textContent.Text;
                    callId = null;
                    return true;
            }
        }

        text = string.Empty;
        callId = null;
        return false;
    }

    public static string? FormatContentValue(object? value)
    {
        return value switch
        {
            null => null,
            string s => s,
            JsonElement jsonElement => jsonElement.ValueKind == JsonValueKind.String
                ? jsonElement.GetString()
                : jsonElement.GetRawText(),
            _ => value.ToString()
        };
    }
}
