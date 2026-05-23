using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace KnowledgeSystem.Provider;

/// <summary>
///     Centralizes construction of OpenAI-backed chat clients.
/// </summary>
public static class OpenAiChatClientFactory
{
    public static IChatClient Create(string endpoint, string apiKey, string model)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
        };

        var credentials = new ApiKeyCredential(apiKey);
        var client = new OpenAIClient(credentials, clientOptions);
        return client.GetChatClient(model).AsIChatClient();
    }
}
