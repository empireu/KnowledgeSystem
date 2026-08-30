using System.ClientModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using AssistantChatMessage = OpenAI.Chat.AssistantChatMessage;
using ChatMessageContentPart = OpenAI.Chat.ChatMessageContentPart;
using ChatToolCall = OpenAI.Chat.ChatToolCall;

namespace KnowledgeSystem.Ai;

/// <summary>
///     DeepSeek's thinking mode returns the chain of thought via <c>reasoning_content</c> and requires it to be passed back on every subsequent request that carries tools, or the API returns an error. 
///     The OpenAI adapter drops <see cref="TextReasoningContent"/> when serializing history, so we re-attach it to the outgoing assistant messages.
/// </summary>
public sealed class DeepseekChatClient(IChatClient inner) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await inner.GetResponseAsync(AttachReasoning(chatMessages), options, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        return inner.GetStreamingResponseAsync(AttachReasoning(chatMessages), options, cancellationToken);
    }

    private static IEnumerable<ChatMessage> AttachReasoning(IEnumerable<ChatMessage> chatMessages)
    {
        foreach (var message in chatMessages)
        {
            if (message.Role != ChatRole.Assistant || message.Contents.Count == 0)
            {
                yield return message;
                continue;
            }

            if (!message.Contents.OfType<TextReasoningContent>().Any(r => r.Text.Length > 0))
            {
                yield return message;
                continue;
            }

            var copy = new ChatMessage(message.Role, message.Contents);
            copy.RawRepresentation = BuildAssistantMessage(message);
            yield return copy;
        }
    }

#pragma warning disable SCME0001
    private static AssistantChatMessage BuildAssistantMessage(ChatMessage message)
    {
        var parts = new List<ChatMessageContentPart>();
        var toolCalls = new List<ChatToolCall>();
        var reasoning = new StringBuilder();

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextReasoningContent reasoningContent:
                    reasoning.Append(reasoningContent.Text);
                    break;
                case TextContent textContent:
                    parts.Add(ChatMessageContentPart.CreateTextPart(textContent.Text));
                    break;
                case FunctionCallContent functionCall:
                    toolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                        functionCall.CallId,
                        functionCall.Name,
                        new BinaryData(JsonSerializer.SerializeToUtf8Bytes(functionCall.Arguments))
                    ));
                    break;
            }
        }

        if (parts.Count == 0)
        {
            parts.Add(ChatMessageContentPart.CreateTextPart(string.Empty));
        }

        var assistant = new AssistantChatMessage(parts);

        for (var index = 0; index < toolCalls.Count; index++)
        {
            assistant.ToolCalls.Add(toolCalls[index]);
        }

        ref var patch = ref assistant.Patch;
        patch.Set("$.reasoning_content"u8, JsonSerializer.SerializeToUtf8Bytes(reasoning.ToString()));

        return assistant;
    }
#pragma warning restore SCME0001

    public void Dispose()
    {
        inner.Dispose();
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return inner.GetService(serviceType, serviceKey);
    }
}
