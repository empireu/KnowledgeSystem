using Microsoft.Extensions.AI;
// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Ai;

public sealed class DeepseekChatClient(IChatClient inner) : IChatClient
{
    private const string ReasoningContentKey = "reasoning_content";
    private const string SentinelKey = "__deepseek_reasoning";

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var messagesSnapshot = chatMessages.ToList();

        for (var index = 0; index < messagesSnapshot.Count; index++)
        {
            var message = messagesSnapshot[index];
            if (message.Role != ChatRole.Assistant || message.Contents.Count == 0)
            {
                continue;
            }

            var sentinel = message.Contents
                .OfType<TextContent>()
                .FirstOrDefault(t => t.AdditionalProperties?.ContainsKey(SentinelKey) == true);

            if (sentinel == null)
            {
                continue;
            }

            message.Contents.Remove(sentinel);
            message.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            message.AdditionalProperties[ReasoningContentKey] = sentinel.Text;
        }

        var response = await inner.GetResponseAsync(messagesSnapshot, options, cancellationToken);

        // On receive: store reasoning_content both in AdditionalProperties
        // (for M.E.AI.OpenAI compat) and as a sentinel TextContent (for
        // guaranteed round-trip survival).
        foreach (var msg in response.Messages)
        {
            if (msg.Role != ChatRole.Assistant)
            {
                continue;
            }

            if (msg.AdditionalProperties?.TryGetValue(ReasoningContentKey, out var rc) == true && rc is string rcStr && rcStr.Length > 0)
            {
                var textContent = new TextContent(rcStr);
                textContent.AdditionalProperties ??= new AdditionalPropertiesDictionary();
                textContent.AdditionalProperties[SentinelKey] = true;
                msg.Contents.Add(textContent);
            }
        }

        return response;
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        return inner.GetStreamingResponseAsync(chatMessages, options, cancellationToken);
    }

    public void Dispose()
    {
        inner.Dispose();
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return inner.GetService(serviceType, serviceKey);
    }

}