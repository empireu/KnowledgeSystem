using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace KnowledgeSystem.Ai;

/// <summary>
///     Centralizes construction of OpenAI-backed chat clients.
/// </summary>
public static class OpenAiChatClientFactory
{
    public static IChatClient Create(ProviderConfig providerConfig)
    {
        if (providerConfig.ProviderType != ProviderType.Usual)
        {
            throw new NotSupportedException("provider is not implemented");
        }
        
        return Create(providerConfig.Endpoint, providerConfig.Key, providerConfig.Model);
    }
    
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
