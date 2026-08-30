using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;

namespace KnowledgeSystem.Ai;

/// <summary>
///     Centralizes construction of OpenAI-backed chat clients.
/// </summary>
public static class OpenAiChatClientFactory
{
    private const int DefaultNetworkTimeoutSeconds = 600;

    public static IChatClient Create(ProviderConfig providerConfig)
    {
        var client = Create(
            providerConfig.Endpoint,
            providerConfig.Key,
            providerConfig.Model,
            providerConfig.NetworkTimeoutSeconds ?? DefaultNetworkTimeoutSeconds
        );

        return providerConfig.ProviderType switch
        {
            ProviderType.Usual => client,
            ProviderType.Deepseek => new DeepseekChatClient(client),
            _ => throw new NotSupportedException($"Provider type {providerConfig.ProviderType} is not implemented")
        };
    }
    
    public static IChatClient Create(string endpoint, string apiKey, string model, int? networkTimeoutSeconds = null)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            NetworkTimeout = TimeSpan.FromSeconds(networkTimeoutSeconds ?? DefaultNetworkTimeoutSeconds),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
        };

        var credentials = new ApiKeyCredential(apiKey);
        var client = new OpenAIClient(credentials, clientOptions);
        return client.GetChatClient(model).AsIChatClient();
    }
}
